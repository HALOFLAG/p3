// Phase 3 v1.12 Stage 4 — IsLegalPlacement 整合 tag 配對驗證。
// 規格書 §1.5 / §3.1.4 — 候選 tile 與所有相鄰已放置 tile 的 tags 必須兩兩 TagsCompatible。
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapPlacementTagTests
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
    public void IsLegalPlacement_TagMismatch_ReturnsFalse()
    {
        // 起點 (5,5) = village-square (tags: village, outdoor)
        // 持有 underground-passage (tags: underground) — 與起點無共享 tag
        var (_, state, map) = NewStateBackedMap();
        state.CurrentPlayer.HeldTileId = "underground-passage";

        // (5,6) 唯一相鄰已放是 village-square — tag mismatch 應拒絕
        map.IsLegalPlacement(5, 6).Should().BeFalse();
    }

    [Fact]
    public void IsLegalPlacement_BridgeTile_AllowsCrossDistrict()
    {
        // 配置：起點 (5,5) village-square (village, outdoor)；(6,5) 手動放 mansion-parlor (indoor)；
        //      (5,6) 手動放 mansion-front-yard (outdoor)。
        // 候選 (6,6) 的相鄰已放 = mansion-parlor (indoor) + mansion-front-yard (outdoor)（兩個不同 district）
        // 持有 mansion-foyer (outdoor + indoor) — 同時 bridge 兩邊 → 應允許
        var (_, state, map) = NewStateBackedMap();
        // state.TileMap key = (X=col, Y=row)
        state.TileMap[(5, 6)] = new PlacedTile { TileId = "mansion-parlor",     Level = ExplorationLevel.Unfamiliar };
        state.TileMap[(6, 5)] = new PlacedTile { TileId = "mansion-front-yard", Level = ExplorationLevel.Unfamiliar };
        state.CurrentPlayer.HeldTileId = "mansion-foyer";

        map.IsLegalPlacement(6, 6).Should().BeTrue();
    }

    [Fact]
    public void IsLegalPlacement_StandaloneMode_FallsBackToBasic()
    {
        // Standalone 模式：無 GameState / Module → 不檢查 tag，只看相鄰是否有已放置
        var map = new WorldMap(new NoSubstituteRandom());
        // 起點 (5,5) 已放（建構子預設）—相鄰格應 legal，不依賴 tag
        map.BeginMapExpand(); // 設置 _heldTile（standalone）
        map.IsLegalPlacement(4, 5).Should().BeTrue();
        map.IsLegalPlacement(5, 6).Should().BeTrue();
    }
}
