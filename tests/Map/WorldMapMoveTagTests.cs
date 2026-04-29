// Phase 3 v1.12 Stage 8 — 移動 tag 配對驗證（規格書 §1.5 / §3.1.4）。
// 玩家從 A 走到相鄰 B：A.tags 與 B.tags 需 TagsCompatible（OR 邏輯）；
// MapPathFinding BFS 同步檢查跨區（如 outdoor → underground）強制走橋接 tile（mansion-foyer / hidden-chamber）。
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapMoveTagTests
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

    [Fact]
    public void IsLegalMoveTarget_TagMismatch_ReturnsFalse()
    {
        // 起點 (5,5) = village-square (tags: village, outdoor)；
        // 在 (5,6) 手動放 underground-passage (tags: underground) — 與起點無共享 tag
        // → IsLegalMoveTarget 應拒絕
        var (_, state, _) = NewStateBackedMap();
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "underground-passage", Level = ExplorationLevel.Familiar };
        var map = new WorldMap(state, ((ModuleLoadResult.Success)new ModuleLoader(TestPaths.SchemasFolder)
            .Load(TestPaths.AbandonedMansionFolder)).Module, new NoSubstituteRandom());

        map.IsLegalMoveTarget(5, 6).Should().BeFalse();

        // 對照組：放 mansion-foyer (outdoor + indoor) 在 (5,6) — 與起點 outdoor 共享 → 應允許
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-foyer", Level = ExplorationLevel.Familiar };
        map.IsLegalMoveTarget(5, 6).Should().BeTrue();
    }

    [Fact]
    public void MapPathFinding_FindPath_StopsAtTagBoundary()
    {
        // 起點 (5,5) village-square (village, outdoor)
        // 鋪設一條 path：(5,6) underground-passage → (5,7) ritual-hall（皆 underground）
        // 起點 → (5,6) tag mismatch → BFS 找不到路
        var (module, state, _) = NewStateBackedMap();
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "underground-passage", Level = ExplorationLevel.Familiar };
        state.TileMap[(7, 5)] = new PlacedTile { TileId = "ritual-hall",         Level = ExplorationLevel.Familiar };

        var pf = new MapPathFinding();
        var path = pf.FindPath(state, new Position(5, 5), new Position(7, 5), module);

        path.Should().BeEmpty(); // tag boundary 阻擋 BFS 擴展
    }

    [Fact]
    public void MapPathFinding_FindPath_BridgeTileAllowsCrossing()
    {
        // 起點 (5,5) village-square (village, outdoor)
        // (5,6) mansion-foyer (outdoor + indoor) — bridge
        // (5,7) hidden-chamber (indoor + underground) — bridge
        // (5,8) ritual-hall (underground)
        // 路徑：start → foyer (共享 outdoor) → hidden-chamber (共享 indoor) → ritual-hall (共享 underground)
        var (module, state, _) = NewStateBackedMap();
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-foyer",   Level = ExplorationLevel.Familiar };
        state.TileMap[(7, 5)] = new PlacedTile { TileId = "hidden-chamber",  Level = ExplorationLevel.Familiar };
        state.TileMap[(8, 5)] = new PlacedTile { TileId = "ritual-hall",     Level = ExplorationLevel.Familiar };

        var pf = new MapPathFinding();
        var path = pf.FindPath(state, new Position(5, 5), new Position(8, 5), module);

        path.Should().HaveCount(3);
        path[0].Should().Be(new Position(6, 5));
        path[1].Should().Be(new Position(7, 5));
        path[2].Should().Be(new Position(8, 5));
    }
}
