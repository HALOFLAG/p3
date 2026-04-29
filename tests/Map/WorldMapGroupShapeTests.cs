// Phase 3 v1.12 Stage 7 — 地塊組 GroupShape 強制拓撲驗證（規格書 §1.5 / §3.1.4）。
// rectangle:WxH 限定 bounding box 不超過 W×H；line:N 限定共線（橫或縱）且 span ≤ N。
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapGroupShapeTests
{
    private sealed class NoSubstituteRandom : IRandomProvider
    {
        public double NextDouble() => 0.99;
        public int Next(int maxExclusive) => 0;
    }

    private static (Module module, GameState state, WorldMap map) NewWithTile(string tileIdInBatch)
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
        // 換成只含目標 tile 的批次
        state.PendingTileBatches.Clear();
        state.TileChoiceBatch.Clear();
        state.TileChoiceBatch.Add(tileIdInBatch);
        var map = new WorldMap(state, module, new NoSubstituteRandom());
        return (module, state, map);
    }

    [Fact]
    public void GroupShape_Rectangle2x2_LimitsNextSlotToExpectedPosition()
    {
        // mansion-grand-foyer: groupCount=4, groupShape="rectangle:2x2"
        // 預先在 (col=6, row=5) 放 mansion-parlor (indoor) 解 tag check（與 indoor mansion-grand-foyer 相容）
        var (_, state, map) = NewWithTile("mansion-grand-foyer");
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-parlor", Level = ExplorationLevel.Familiar };

        map.BeginMapExpand();
        map.SelectFromBatch(0);

        // 第 1 格放 (5, 7) — col=7, row=5
        map.TryPlaceHeldTile(5, 7).Should().BeTrue();
        // 第 2 格放 (5, 8) — col=8, row=5。Box [7..8, 5..5] = 2×1 ⊆ 2×2 ✓
        map.TryPlaceHeldTile(5, 8).Should().BeTrue();
        // 第 3 格只能放 (4,7)/(4,8)（讓 box 變 2×2）— 不可放 (5,9) 會讓 box 變 3×1
        map.IsLegalPlacement(5, 9).Should().BeFalse(); // box 3×1 超過 2×2
        map.IsLegalPlacement(4, 7).Should().BeTrue();  // box [7..8, 4..5] = 2×2 ⊆ 2×2 ✓
        map.TryPlaceHeldTile(4, 7).Should().BeTrue();
        // 第 4 格只剩 (4, 8)（補齊 2×2 的最後一角）
        map.IsLegalPlacement(4, 8).Should().BeTrue();
        // 嘗試放任何超出 2×2 box 的格子應拒絕
        map.IsLegalPlacement(3, 7).Should().BeFalse(); // box [7..8, 3..5] = 2×3 → 但 2×3 不能裝入 2×2 → ✗
        map.TryPlaceHeldTile(4, 8).Should().BeTrue();
        // 4 格完成
        state.PendingGroupCells.Should().BeEmpty();
        map.Mode.Should().Be(InteractionMode.Idle);
    }

    [Fact]
    public void GroupShape_LineN_AllowsHorizontalOrVertical()
    {
        // grand-hallway: groupCount=2, groupShape="line:2"
        // 兩種情境：先水平放完一次、再驗證垂直放完一次（用兩個 fixture 各跑一次）

        // === 水平 ===
        {
            var (_, state, map) = NewWithTile("grand-hallway");
            state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-parlor", Level = ExplorationLevel.Familiar };
            map.BeginMapExpand();
            map.SelectFromBatch(0);
            map.TryPlaceHeldTile(5, 7).Should().BeTrue();          // 第 1 格 (col=7, row=5)
            map.IsLegalPlacement(5, 8).Should().BeTrue();          // 水平 → ✓
            map.TryPlaceHeldTile(5, 8).Should().BeTrue();          // 完成
            state.PendingGroupCells.Should().BeEmpty();
            map.Mode.Should().Be(InteractionMode.Idle);
        }

        // === 垂直 ===
        {
            var (_, state, map) = NewWithTile("grand-hallway");
            state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-parlor", Level = ExplorationLevel.Familiar };
            map.BeginMapExpand();
            map.SelectFromBatch(0);
            map.TryPlaceHeldTile(5, 7).Should().BeTrue();          // 第 1 格 (col=7, row=5)
            map.IsLegalPlacement(4, 7).Should().BeTrue();          // 垂直（向上）→ ✓
            map.TryPlaceHeldTile(4, 7).Should().BeTrue();          // 完成
            state.PendingGroupCells.Should().BeEmpty();
            map.Mode.Should().Be(InteractionMode.Idle);
        }
    }
}
