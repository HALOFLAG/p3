// Phase 2 任務 11 Stage 6 — L3-07「事件變化地塊 → 三視角同步」整合測試。
//
// 規格書 L3-07：事件 trigger → tile-side transformation → 三視角（主地圖、場景立繪、小地圖）
// 同步反映新地形。Godot UI 側無法在 xUnit 直接驗，本測試驗事件層：
//   - state.TileMap 寫入新 TileId
//   - WorldMap.GetTile 回傳新 MapTerrain
//   - TileChanged 事件以正確 (row, col) emit（→ 主地圖 + 小地圖訂閱者）
//   - TileTransformed 事件以正確 (row, col, oldT, newT) emit（→ 翻牌動畫 hook）
//
// 場景：玩家所在格放 village-store；resolve village-inquiry → tile-side
//       transformation 觸發 village-store → forest-path（demo data，Stage 5 加入）。
using CardNarrative.Core.Cards;
using CardNarrative.Core.Events;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Integration;

public class L3_07_TileTransformThreeViewSyncTests
{
    private sealed class NoSubstituteRandom : IRandomProvider
    {
        public double NextDouble() => 0.99;
        public int Next(int maxExclusive) => 0;
    }

    [Fact]
    public void L3_07_TileSideTransformation_TriggersBothTileChangedAndTileTransformed()
    {
        // === 場景建立 ===
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var module = ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;
        var heroId = module.Characters.Keys.First();
        var state = GameState.CreateNew(
            module, new[] { heroId },
            module.Prologue.StartingCompanionIds, seed: 1234,
            gridSize: 9, startPosition: new Position(4, 4));
        var worldMap = new WorldMap(state, module, new NoSubstituteRandom());
        var registry = TileTransformRegistry.Build(module);

        // 模擬玩家剛 MapExpand 放 village-store 在 (4,5)
        state.TileMap[(5, 4)] = new PlacedTile { TileId = "village-store", Level = ExplorationLevel.Familiar };
        // 確認初始狀態
        worldMap.GetTile(4, 5).Terrain.Should().Be(MapTerrain.Building);

        // === 事件監聽 ===
        var tileChangedEvents = new List<(int row, int col)>();
        var tileTransformedEvents = new List<(int row, int col, MapTerrain oldT, MapTerrain newT)>();
        worldMap.TileChanged += (r, c) => tileChangedEvents.Add((r, c));
        worldMap.TileTransformed += (r, c, o, n) => tileTransformedEvents.Add((r, c, o, n));

        // === 模擬 MainBootstrap.OnEventResolved 完整流程 ===
        worldMap.BeginEventBatch();
        try
        {
            // 1. 抓 OLD terrain（前置 snapshot）
            var preStates = new List<(int Row, int Col, MapTerrain OldTerrain)>();
            foreach (var (sourceTileId, _) in registry.GetRulesForEvent("village-inquiry"))
            {
                foreach (var (key, placed) in state.TileMap)
                {
                    if (placed.TileId != sourceTileId) continue;
                    var oldTile = module.Tiles[placed.TileId];
                    var oldTerrain = TileVisualProfileResolver.ResolveTerrain(oldTile);
                    preStates.Add((key.Y, key.X, oldTerrain));
                }
            }

            // 2. 套用 tile-side transformations
            var ctx = JsonLogicContextBuilder.FromGameState(state, module);
            var changes = MapService.TryTransformTilesForEvent(state, module, registry, "village-inquiry", ctx);

            // 3. emit TileChanged + TileTransformed（依 priority 排序）
            foreach (var change in changes)
            {
                int row = change.Y, col = change.X;
                worldMap.NotifyTileChanged(row, col);
                var newTile = module.Tiles[state.TileMap[(col, row)].TileId];
                var newTerrain = TileVisualProfileResolver.ResolveTerrain(newTile);
                MapTerrain? oldT = null;
                foreach (var ps in preStates)
                {
                    if (ps.Row == row && ps.Col == col) { oldT = ps.OldTerrain; break; }
                }
                if (oldT.HasValue)
                    worldMap.NotifyTileTransformed(row, col, oldT.Value, newTerrain);
            }
        }
        finally
        {
            worldMap.EndEventBatch();
        }

        // === 驗證：state 已 mutate ===
        state.TileMap[(5, 4)].TileId.Should().Be("forest-path");
        // GetTile 回新 terrain（主地圖 / 小地圖讀此）
        worldMap.GetTile(4, 5).Terrain.Should().Be(MapTerrain.Forest);

        // === 驗證：兩個事件都 emit 一次（priority 排序：TileChanged 先、TileTransformed 後）===
        tileChangedEvents.Should().HaveCount(1);
        tileChangedEvents[0].Should().Be((4, 5));
        tileTransformedEvents.Should().HaveCount(1);
        tileTransformedEvents[0].Should().Be((4, 5, MapTerrain.Building, MapTerrain.Forest));
    }

    [Fact]
    public void L3_07_NoMatchingTilePlaced_NoEventsEmitted()
    {
        // 確認：沒有 SourceTileId 的格放在地圖上時，事件不誤觸發
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var module = ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;
        var state = GameState.CreateNew(
            module, new[] { module.Characters.Keys.First() },
            module.Prologue.StartingCompanionIds, seed: 1234,
            gridSize: 9, startPosition: new Position(4, 4));
        var worldMap = new WorldMap(state, module, new NoSubstituteRandom());
        var registry = TileTransformRegistry.Build(module);

        var transformedCount = 0;
        worldMap.TileTransformed += (_, _, _, _) => transformedCount++;

        // 玩家所在 (4,4) 是 village-square，不是 village-store；無 transform 規則
        worldMap.BeginEventBatch();
        var ctx = JsonLogicContextBuilder.FromGameState(state, module);
        var changes = MapService.TryTransformTilesForEvent(state, module, registry, "village-inquiry", ctx);
        worldMap.EndEventBatch();

        changes.Should().BeEmpty();
        transformedCount.Should().Be(0);
    }

    [Fact]
    public void L3_07_TransformTilePlayerPos_SubsequentGetTileReflectsNewTerrain()
    {
        // 模擬玩家踩在 village-store 上（不可能但測試用），transformTile 後該格新地形對 GetTile 可見
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var module = ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;
        var state = GameState.CreateNew(
            module, new[] { module.Characters.Keys.First() },
            module.Prologue.StartingCompanionIds, seed: 1234,
            gridSize: 9, startPosition: new Position(4, 4));
        var worldMap = new WorldMap(state, module, new NoSubstituteRandom());
        var registry = TileTransformRegistry.Build(module);

        // 直接把起始格替換成 village-store（測試用 fixture）
        state.TileMap[(4, 4)].TileId = "village-store";
        state.TileMap[(4, 4)].Level = ExplorationLevel.Familiar;
        worldMap.GetTile(4, 4).Terrain.Should().Be(MapTerrain.Building);

        var changes = MapService.TryTransformTilesForEvent(
            state, module, registry, "village-inquiry",
            JsonLogicContextBuilder.FromGameState(state, module));

        changes.Should().HaveCount(1);
        // GetTile 反映新 TileId → Forest
        worldMap.GetTile(4, 4).Terrain.Should().Be(MapTerrain.Forest);
        // ParallaxScene 該以 Forest 渲染（runtime 側由 MainMapRenderer.OnTileChanged → UpdateParallaxScene 處理；
        // 本測試不能直接驗 Godot UI，但 GetTile 已是 single SoT）
    }
}
