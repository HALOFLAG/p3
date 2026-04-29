// Phase 2 任務 11 Stage 2c — WorldMap dual-mode（GetTile + Hand 投影）測試。
// 驗證：
// - state-mode GetTile 從 state.TileMap 取，透過 TileVisualProfileResolver 翻譯
// - 缺格回 IsPlaced=false
// - IsExplored 從 PlacedTile.Level >= Familiar 派生
// - state-mode Hand 從 state.CurrentPlayer.Hand × module.ActionCards 投影
// - 投影即時反映 state.Hand 變更
// - standalone mode 仍走內部 _tiles / _hand
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapDualModeTilesAndHandTests
{
    private sealed class NoSubstituteRandom : IRandomProvider
    {
        public double NextDouble() => 0.99;
        public int Next(int maxExclusive) => 0;
    }

    private static (Module module, GameState state, WorldMap map) NewStateBackedMap()
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
        var map = new WorldMap(state, module, new NoSubstituteRandom());
        return (module, state, map);
    }

    // === GetTile dispatch ===

    [Fact]
    public void StateMode_GetTile_AtStartingPos_ReturnsResolvedTileExplored()
    {
        var (module, state, map) = NewStateBackedMap();
        // 起始 tile = village-square (terrain=town, visualProfile.terrain=Building)；v1.13 中心 (5,5)
        var tile = map.GetTile(5, 5);
        tile.IsPlaced.Should().BeTrue();
        tile.Row.Should().Be(5);
        tile.Col.Should().Be(5);
        tile.Terrain.Should().Be(MapTerrain.Building);
        // Stage 3.6 修復：CreateNew 預設 Level=Unfamiliar；
        // IsExplored 門檻改為 >= Unfamiliar（語意：placed-and-entered = 已知）→ 起始格 IsExplored=true
        // 這對應 Phase 1+2 standalone 起始格 IsExplored=true 的行為，避免起始格顯示 card-back
        tile.IsExplored.Should().BeTrue();
    }

    [Fact]
    public void StateMode_GetTile_EmptyCell_ReturnsNotPlaced()
    {
        var (_, _, map) = NewStateBackedMap();
        var tile = map.GetTile(0, 0);
        tile.IsPlaced.Should().BeFalse();
        tile.IsExplored.Should().BeFalse();
    }

    [Fact]
    public void StateMode_GetTile_FamiliarLevel_IsExplored()
    {
        var (_, state, map) = NewStateBackedMap();
        // 把起始格升到 Familiar（v1.13 中心 (5,5)）
        state.TileMap[(5, 5)].Level = ExplorationLevel.Familiar;
        var tile = map.GetTile(5, 5);
        tile.IsExplored.Should().BeTrue();
    }

    [Fact]
    public void StateMode_GetTile_AfterTileMapMutation_ReflectsState()
    {
        var (module, state, map) = NewStateBackedMap();
        // 動態加一格 forest-path（terrain=wilderness, visualProfile.terrain=Forest）
        state.TileMap[(3, 4)] = new PlacedTile { TileId = "forest-path" };
        var tile = map.GetTile(4, 3); // Row=4, Col=3 → state(X=3, Y=4)
        tile.IsPlaced.Should().BeTrue();
        tile.Terrain.Should().Be(MapTerrain.Forest);
    }

    [Fact]
    public void StateMode_GetTile_UnknownTileId_ReturnsNotPlaced()
    {
        var (module, state, map) = NewStateBackedMap();
        // 故意塞一個 module 沒有的 tileId
        state.TileMap[(0, 0)] = new PlacedTile { TileId = "nonexistent-tile-id" };
        var tile = map.GetTile(0, 0);
        // 缺 module.Tiles entry → 視為未放置（防禦）
        tile.IsPlaced.Should().BeFalse();
    }

    // === Hand 投影 ===

    [Fact]
    public void StateMode_Hand_EmptyByDefault()
    {
        var (_, _, map) = NewStateBackedMap();
        // CreateNew 不發牌 — Hand 應為空
        map.Hand.Should().BeEmpty();
    }

    [Fact]
    public void StateMode_Hand_ReflectsStateHand()
    {
        var (module, state, map) = NewStateBackedMap();
        // 從 module 取一張 action card 加進 state.Hand
        var firstCardId = module.ActionCards.Keys.First();
        state.CurrentPlayer.Hand.Add(firstCardId);

        map.Hand.Should().HaveCount(1);
        map.Hand[0].Id.Should().Be(firstCardId);
    }

    [Fact]
    public void StateMode_Hand_SkipsMissingCardIds()
    {
        var (module, state, map) = NewStateBackedMap();
        var validId = module.ActionCards.Keys.First();
        state.CurrentPlayer.Hand.Add(validId);
        state.CurrentPlayer.Hand.Add("ghost-card"); // 不存在 module — 應略過
        state.CurrentPlayer.Hand.Add(module.ActionCards.Keys.Skip(1).First());

        // 投影應只含 2 張有效卡（ghost-card 略過）
        map.Hand.Should().HaveCount(2);
    }

    [Fact]
    public void StateMode_Hand_ProjectionReflectsLiveStateChanges()
    {
        var (module, state, map) = NewStateBackedMap();
        var cardId = module.ActionCards.Keys.First();

        map.Hand.Should().BeEmpty();
        state.CurrentPlayer.Hand.Add(cardId);
        map.Hand.Should().HaveCount(1);
        state.CurrentPlayer.Hand.Clear();
        map.Hand.Should().BeEmpty();
    }

    // === Standalone mode 不變 ===

    [Fact]
    public void StandaloneMode_GetTile_UsesInternalTiles()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        // standalone 預設 (5,5)（v1.13 中心）是 Building 已放置
        var tile = map.GetTile(WorldMap.InitialPlayerRow, WorldMap.InitialPlayerCol);
        tile.IsPlaced.Should().BeTrue();
        tile.Terrain.Should().Be(MapTerrain.Building);
    }

    [Fact]
    public void StandaloneMode_Hand_UsesInternalHand()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        map.Hand.Should().BeEmpty(); // 沒 LoadActionDeck 仍為空
    }
}
