// Phase 2 任務 11 Stage 3.5 — ORBIT outcome → EffectHandler → WorldMap.NotifyTileChanged
// 端到端整合測試（不含 Godot UI 層）。
//
// 模擬 MainBootstrap.OnEventResolved 的核心流程：
// 1. 取 EventInstance 對應 outcome（依 tier）
// 2. WorldMap.BeginEventBatch
// 3. 對 outcome.Effects 逐項 EffectHandler.Apply(state, module)
// 4. 對 TransformTileEffect 呼 worldMap.NotifyTileChanged
// 5. WorldMap.EndEventBatch → flush emit
//
// 驗證：state.TileMap 更新、worldMap.GetTile 反映新 terrain、TileChanged 事件 emit 一次
using CardNarrative.Core.Cards;
using CardNarrative.Core.Events;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Integration;

public class OrbitOutcomeBridgeTests
{
    private sealed class NoSubstituteRandom : IRandomProvider
    {
        public double NextDouble() => 0.99;
        public int Next(int maxExclusive) => 0;
    }

    private static (Module module, GameState state, WorldMap map) NewStateBackedRuntime()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var module = ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;
        var heroId = module.Characters.Keys.First();
        var state = GameState.CreateNew(
            module,
            chosenCharacterIds: new[] { heroId },
            chosenCompanionIds: module.Prologue.StartingCompanionIds,
            seed: 1234,
            gridSize: 9,
            startPosition: new Position(4, 4));
        var map = new WorldMap(state, module, new NoSubstituteRandom());
        return (module, state, map);
    }

    [Fact]
    public void TransformTileEffect_Applied_StateTileMapUpdated_AndTileChangedEmitted()
    {
        var (module, state, map) = NewStateBackedRuntime();
        // 起始 tile = village-square (Building) at (4,4)
        state.TileMap[(4, 4)].TileId.Should().Be("village-square");
        map.GetTile(4, 4).Terrain.Should().Be(MapTerrain.Building);

        // 模擬 outcome：把 (4,4) 變成 forest-path（visualProfile=Forest）
        var effect = new TransformTileEffect("forest-path", X: 4, Y: 4);
        var changedEvents = new List<(int row, int col)>();
        map.TileChanged += (r, c) => changedEvents.Add((r, c));

        // 模擬 OnEventResolved 的 batch 包裹
        map.BeginEventBatch();
        try
        {
            new EffectHandler().Apply(effect, state, module);
            map.NotifyTileChanged(4, 4); // row=Y=4, col=X=4
        }
        finally
        {
            map.EndEventBatch();
        }

        // state.TileMap 已換成 forest-path
        state.TileMap[(4, 4)].TileId.Should().Be("forest-path");
        // GetTile 反映新 terrain
        map.GetTile(4, 4).Terrain.Should().Be(MapTerrain.Forest);
        // TileChanged 事件 emit 一次
        changedEvents.Should().HaveCount(1);
        changedEvents[0].Should().Be((4, 4));
    }

    [Fact]
    public void TransformTilesByTagEffect_Applied_AllMatchingTilesEmitTileChanged()
    {
        var (module, state, map) = NewStateBackedRuntime();
        // 加幾格 outdoor wilderness tile
        state.TileMap[(3, 4)] = new PlacedTile { TileId = "forest-path", Level = ExplorationLevel.Familiar };
        state.TileMap[(5, 4)] = new PlacedTile { TileId = "misty-forest", Level = ExplorationLevel.Familiar };
        // (4,4) village-square 是 town tag，不在 outdoor 範圍

        var effect = new TransformTilesByTagEffect(
            Tags: new[] { "outdoor" },
            NewTileId: "mansion-front-yard"); // mansion-front-yard 是 outdoor + visualProfile=Grass

        var changedEvents = new List<(int row, int col)>();
        map.TileChanged += (r, c) => changedEvents.Add((r, c));

        map.BeginEventBatch();
        try
        {
            new EffectHandler().Apply(effect, state, module);
            // 模擬 MainBootstrap 的 NotifyWorldMapAfterEffect 對 TransformTilesByTag 的處理
            foreach (var (key, placed) in state.TileMap)
            {
                if (!module.Tiles.TryGetValue(placed.TileId, out var t)) continue;
                if (effect.Tags.All(tag => t.Tags.Contains(tag)))
                    map.NotifyTileChanged(key.Y, key.X);
            }
        }
        finally
        {
            map.EndEventBatch();
        }

        // 三格都有 "outdoor" tag（village-square: village+outdoor / forest-path / misty-forest）
        // → 全部變成 mansion-front-yard
        state.TileMap[(3, 4)].TileId.Should().Be("mansion-front-yard");
        state.TileMap[(4, 4)].TileId.Should().Be("mansion-front-yard");
        state.TileMap[(5, 4)].TileId.Should().Be("mansion-front-yard");

        // 事件至少 3 個（三格都受影響）
        changedEvents.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void EventBatch_MultipleEffects_FlushOnceInPriorityOrder()
    {
        var (module, state, map) = NewStateBackedRuntime();
        var emitOrder = new List<string>();
        map.TileChanged += (_, _) => emitOrder.Add("TileChanged");
        map.HpChanged += _ => emitOrder.Add("HpChanged");

        map.BeginEventBatch();
        // 故意先 enqueue HpChanged 再 TileChanged
        map.NotifyHpChanged(3);
        map.NotifyTileChanged(4, 4);
        map.NotifyHpChanged(5);
        emitOrder.Should().BeEmpty(); // 尚未 flush
        map.EndEventBatch();

        // flush 後依 priority asc：TileChanged(10) → HpChanged(30) ×2
        emitOrder.Should().ContainInOrder("TileChanged", "HpChanged", "HpChanged");
    }
}
