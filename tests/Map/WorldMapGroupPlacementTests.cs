// Phase 3 v1.12 Stage 6 — 地塊組連續放置驗證（規格書 §1.5 / §3.1.4）。
// 一張卡 = N 個 1×1 cell，玩家從同卡選 N 格自由連通放置；放置中限定相鄰、Cancel rollback、完成清狀態。
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapGroupPlacementTests
{
    private sealed class NoSubstituteRandom : IRandomProvider
    {
        public double NextDouble() => 0.99;
        public int Next(int maxExclusive) => 0;
    }

    /// <summary>建構 abandoned-mansion 模組 + state，並把 batch 換成只含 mansion-grand-foyer（4 格 group）以利測試。</summary>
    private static (Module module, GameState state, WorldMap map) NewWithGroupTile()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var module = ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;
        var heroId = module.Characters.Keys.First();
        var state = GameState.CreateNew(
            module,
            chosenCharacterIds: new[] { heroId },
            chosenCompanionIds: module.Prologue.StartingCompanionIds,
            seed: 1234,
            gridSize: 11,
            startPosition: new Position(5, 5));
        // 清空 prologue 的 tileBatches，自己塞一個只含 mansion-grand-foyer 的批次
        state.PendingTileBatches.Clear();
        state.TileChoiceBatch.Clear();
        state.TileChoiceBatch.Add("mansion-grand-foyer");
        var map = new WorldMap(state, module, new NoSubstituteRandom());
        return (module, state, map);
    }

    /// <summary>建構含單格 tile（mansion-parlor，groupCount=1）的批次以驗證單格行為。</summary>
    private static (Module module, GameState state, WorldMap map) NewWithSingleCellTile()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var module = ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;
        var heroId = module.Characters.Keys.First();
        var state = GameState.CreateNew(
            module,
            chosenCharacterIds: new[] { heroId },
            chosenCompanionIds: module.Prologue.StartingCompanionIds,
            seed: 1234,
            gridSize: 11,
            startPosition: new Position(5, 5));
        state.PendingTileBatches.Clear();
        state.TileChoiceBatch.Clear();
        state.TileChoiceBatch.Add("mansion-parlor"); // groupCount=1（預設），tags=["indoor"]
        var map = new WorldMap(state, module, new NoSubstituteRandom());
        return (module, state, map);
    }

    [Fact]
    public void TileGroup_GroupCountOne_BehavesAsNormalTile()
    {
        // 起始 (5,5) = village-square (village, outdoor)
        // mansion-parlor (indoor) 與 village-square 無共享 tag → 單格放在起點鄰格應拒絕
        // 但 (5,6) 直接放會被 tag check 擋下；改測「state 不會初始化組」即可
        var (_, state, map) = NewWithSingleCellTile();

        map.BeginMapExpand();
        map.SelectFromBatch(0);

        // 單格 tile：選後不應啟動組狀態
        state.PendingGroupTileId.Should().BeNull();
        state.PendingGroupRemaining.Should().Be(0);
        state.PendingGroupCells.Should().BeEmpty();
        state.PendingGroupInstanceId.Should().BeNull();
    }

    [Fact]
    public void SelectFromBatch_GroupTile_InitializesPendingState()
    {
        var (_, state, map) = NewWithGroupTile();

        map.BeginMapExpand();
        map.SelectFromBatch(0); // mansion-grand-foyer (groupCount=4)

        state.CurrentPlayer.HeldTileId.Should().Be("mansion-grand-foyer");
        state.PendingGroupTileId.Should().Be("mansion-grand-foyer");
        state.PendingGroupRemaining.Should().Be(4);
        state.PendingGroupInstanceId.Should().NotBeNull(); // 流水號已分配
        state.PendingGroupCells.Should().BeEmpty();        // 尚未放任何格
    }

    [Fact]
    public void TryPlace_FirstCellOfGroup_DecrementsRemainingTo3()
    {
        // mansion-grand-foyer tags=["indoor"]；起點 village-square tags=["village","outdoor"]
        // 兩者無共享 tag → IsLegalPlacement 第 1 格在鄰格會被 tag check 擋。
        // 為了測試組進度，預先在起點旁手動放一個 indoor tile，之後第 1 格擺它隔壁。
        var (_, state, map) = NewWithGroupTile();
        // 在 (5,6) 手動放 mansion-parlor (indoor) — 起點 (5,5) 的東鄰
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-parlor", Level = ExplorationLevel.Familiar };

        map.BeginMapExpand();
        map.SelectFromBatch(0); // 持有 mansion-grand-foyer

        // 第 1 格放 (5,7)：相鄰 mansion-parlor (indoor)，與 mansion-grand-foyer (indoor) 共享 indoor → ok
        map.TryPlaceHeldTile(5, 7).Should().BeTrue();

        state.PendingGroupRemaining.Should().Be(3);
        state.PendingGroupCells.Should().HaveCount(1);
        state.PendingGroupCells[0].Should().Be((7, 5)); // (X=col=7, Y=row=5)
        // held 不清，Mode 仍 MapExpand
        state.CurrentPlayer.HeldTileId.Should().Be("mansion-grand-foyer");
        map.Mode.Should().Be(InteractionMode.MapExpand);
        // GroupInstanceId 寫入 PlacedTile
        state.TileMap[(7, 5)].GroupInstanceId.Should().Be(state.PendingGroupInstanceId);
    }

    [Fact]
    public void TryPlace_SecondCellMustBeAdjacentToFirst()
    {
        var (_, state, map) = NewWithGroupTile();
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-parlor", Level = ExplorationLevel.Familiar };

        map.BeginMapExpand();
        map.SelectFromBatch(0);
        map.TryPlaceHeldTile(5, 7).Should().BeTrue(); // 第 1 格 (col=7, row=5)

        // 嘗試第 2 格放遠處 (1,1) — 不與第 1 格相鄰 → 拒絕
        map.IsLegalPlacement(1, 1).Should().BeFalse();
        map.TryPlaceHeldTile(1, 1).Should().BeFalse();

        // 第 2 格放 (5,8)：與第 1 格 (5,7) 相鄰 → 接受
        map.IsLegalPlacement(5, 8).Should().BeTrue();
        map.TryPlaceHeldTile(5, 8).Should().BeTrue();
        state.PendingGroupRemaining.Should().Be(2);
        state.PendingGroupCells.Should().HaveCount(2);
    }

    [Fact]
    public void TryPlace_FourthCellCompletesGroup_ResetsState()
    {
        var (_, state, map) = NewWithGroupTile();
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-parlor", Level = ExplorationLevel.Familiar };

        map.BeginMapExpand();
        map.SelectFromBatch(0);
        var groupId = state.PendingGroupInstanceId!.Value;

        // 連放 4 格（自由連通）— 每格與前一格相鄰即可（Stage 6 不強制 shape）
        map.TryPlaceHeldTile(5, 7).Should().BeTrue(); // 1
        map.TryPlaceHeldTile(5, 8).Should().BeTrue(); // 2
        map.TryPlaceHeldTile(4, 8).Should().BeTrue(); // 3
        map.TryPlaceHeldTile(4, 7).Should().BeTrue(); // 4

        // 組完成：狀態清空、Mode → Idle、held 清
        state.PendingGroupTileId.Should().BeNull();
        state.PendingGroupRemaining.Should().Be(0);
        state.PendingGroupCells.Should().BeEmpty();
        state.PendingGroupInstanceId.Should().BeNull();
        state.CurrentPlayer.HeldTileId.Should().BeNull();
        map.Mode.Should().Be(InteractionMode.Idle);

        // 4 格 PlacedTile 共享同 GroupInstanceId
        state.TileMap[(7, 5)].GroupInstanceId.Should().Be(groupId);
        state.TileMap[(8, 5)].GroupInstanceId.Should().Be(groupId);
        state.TileMap[(8, 4)].GroupInstanceId.Should().Be(groupId);
        state.TileMap[(7, 4)].GroupInstanceId.Should().Be(groupId);
    }

    [Fact]
    public void CancelMapExpand_DuringGroup_RollsBackAllCells()
    {
        var (_, state, map) = NewWithGroupTile();
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-parlor", Level = ExplorationLevel.Familiar };

        map.BeginMapExpand();
        map.SelectFromBatch(0);
        map.TryPlaceHeldTile(5, 7).Should().BeTrue();
        map.TryPlaceHeldTile(5, 8).Should().BeTrue();
        // 已放 2 格 (7,5) (8,5)；尚剩 2 格

        map.CancelMapExpand();

        // 已放的 2 格應從 TileMap 移除
        state.TileMap.Should().NotContainKey((7, 5));
        state.TileMap.Should().NotContainKey((8, 5));
        // 起點 + 預設的 mansion-parlor 仍在
        state.TileMap.Should().ContainKey((5, 5));
        state.TileMap.Should().ContainKey((6, 5));
        // 組狀態清空
        state.PendingGroupTileId.Should().BeNull();
        state.PendingGroupRemaining.Should().Be(0);
        state.PendingGroupCells.Should().BeEmpty();
        state.PendingGroupInstanceId.Should().BeNull();
        // held 退回 batch
        state.CurrentPlayer.HeldTileId.Should().BeNull();
        state.TileChoiceBatch.Should().Contain("mansion-grand-foyer");
        map.Mode.Should().Be(InteractionMode.Idle);
    }

    [Fact]
    public void IsLegalPlacement_DuringGroup_OnlyChecksGroupAdjacency()
    {
        // 組進行中第 2+ 格只看「同組已放格相鄰」即可，不需與起點相連、不需 tag check
        var (_, state, map) = NewWithGroupTile();
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-parlor", Level = ExplorationLevel.Familiar };

        map.BeginMapExpand();
        map.SelectFromBatch(0);
        map.TryPlaceHeldTile(5, 7).Should().BeTrue(); // 第 1 格 (col=7, row=5)

        // 第 2+ 格的合法性只看與已放同組格相鄰：
        // (5,8) 與 (5,7) 相鄰 → true
        map.IsLegalPlacement(5, 8).Should().BeTrue();
        // (4,7) 與 (5,7) 相鄰 → true
        map.IsLegalPlacement(4, 7).Should().BeTrue();
        // (3,3) 不相鄰任何同組格 → false
        map.IsLegalPlacement(3, 3).Should().BeFalse();
        // (5,5) 已放（起點） → false（GetTile.IsPlaced）
        map.IsLegalPlacement(5, 5).Should().BeFalse();
    }
}
