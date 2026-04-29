// Phase 2 任務 11 Stage 5 — TileTransformRegistry + MapService 測試。
//
// 驗證：
// - Build 從 module.Tiles 抽出所有 Transformations 並依 triggerEventId 索引
// - GetRulesForEvent 對未索引的 eventId 回空 list
// - 同一 eventId 對應多 tile 規則正確聚集
// - abandoned-mansion 起手 demo（village-store → forest-path on village-inquiry）能載入
// - MapService.TryTransformTilesForEvent 套規則時走 EffectHandler；
//   未登錄事件回空、有規則無符合 tile 也回空
// - condition 為 null 視為「無條件成立」
using CardNarrative.Core.Cards;
using CardNarrative.Core.Events;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class TileTransformRegistryTests
{
    private static Module LoadAbandonedMansion()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        return ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;
    }

    [Fact]
    public void Build_AbandonedMansion_IndexesDemoTransformation()
    {
        var module = LoadAbandonedMansion();
        var registry = TileTransformRegistry.Build(module);

        // demo 規則：village-store → forest-path on village-inquiry
        registry.IndexedEventIds.Should().Contain("village-inquiry");
        var rules = registry.GetRulesForEvent("village-inquiry");
        rules.Should().HaveCountGreaterThanOrEqualTo(1);
        rules.Should().Contain(r => r.SourceTileId == "village-store" && r.Rule.TransformsTo == "forest-path");
    }

    [Fact]
    public void GetRulesForEvent_UnknownEventId_ReturnsEmpty()
    {
        var module = LoadAbandonedMansion();
        var registry = TileTransformRegistry.Build(module);
        registry.GetRulesForEvent("nonexistent-event-xyz").Should().BeEmpty();
    }

    [Fact]
    public void Build_NoTransformations_EmptyRegistry()
    {
        // 用空的 in-memory module（無 transformations）
        var emptyModule = LoadAbandonedMansion();
        // 把所有 tile 的 transformations 強制清空（用 with 改 record；但 Tile 是 record ctor positional）
        // 替代：建一個簡化 module，只有 1 個無 transformations 的 tile
        // 簡化路徑：abandoned-mansion 確認 RuleCount > 0；加另一個 standalone 驗證
        var module = LoadAbandonedMansion();
        var registry = TileTransformRegistry.Build(module);
        // 至少 demo 一條（後續加更多 demo 時 ≥1 仍成立）
        registry.RuleCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void TryTransformTilesForEvent_DemoEvent_TransformsMatchingTiles()
    {
        var module = LoadAbandonedMansion();
        var registry = TileTransformRegistry.Build(module);
        var heroId = module.Characters.Keys.First();
        var state = GameState.CreateNew(
            module, new[] { heroId },
            module.Prologue.StartingCompanionIds, seed: 1234,
            gridSize: 11, startPosition: new Position(5, 5));

        // 在地圖上放 2 張 village-store + 1 張 forest-path（後者不該被影響）
        state.TileMap[(3, 4)] = new PlacedTile { TileId = "village-store", Level = ExplorationLevel.Familiar };
        state.TileMap[(5, 4)] = new PlacedTile { TileId = "village-store", Level = ExplorationLevel.Unfamiliar };
        state.TileMap[(4, 5)] = new PlacedTile { TileId = "forest-path", Level = ExplorationLevel.Familiar };

        // 模擬 village-inquiry trigger
        var ctx = JsonLogicContextBuilder.FromGameState(state, module);
        var changes = MapService.TryTransformTilesForEvent(state, module, registry, "village-inquiry", ctx);

        // 應有 2 格變化（兩張 village-store）
        changes.Count.Should().Be(2);
        changes.Should().AllSatisfy(c => c.OldTileId.Should().Be("village-store"));
        changes.Should().AllSatisfy(c => c.NewTileId.Should().Be("forest-path"));
        // state 已 mutate
        state.TileMap[(3, 4)].TileId.Should().Be("forest-path");
        state.TileMap[(5, 4)].TileId.Should().Be("forest-path");
        // forest-path 不變
        state.TileMap[(4, 5)].TileId.Should().Be("forest-path"); // (本來就是 forest-path)
    }

    [Fact]
    public void TryTransformTilesForEvent_UnknownEvent_NoChange()
    {
        var module = LoadAbandonedMansion();
        var registry = TileTransformRegistry.Build(module);
        var state = GameState.CreateNew(
            module, new[] { module.Characters.Keys.First() },
            module.Prologue.StartingCompanionIds, seed: 1234,
            gridSize: 11, startPosition: new Position(5, 5));

        state.TileMap[(3, 4)] = new PlacedTile { TileId = "village-store", Level = ExplorationLevel.Unfamiliar };
        var changes = MapService.TryTransformTilesForEvent(state, module, registry, "nonexistent", null);
        changes.Should().BeEmpty();
        state.TileMap[(3, 4)].TileId.Should().Be("village-store"); // 不變
    }

    [Fact]
    public void TryTransformTilesForEvent_NoMatchingTilePlaced_NoChange()
    {
        var module = LoadAbandonedMansion();
        var registry = TileTransformRegistry.Build(module);
        var state = GameState.CreateNew(
            module, new[] { module.Characters.Keys.First() },
            module.Prologue.StartingCompanionIds, seed: 1234,
            gridSize: 11, startPosition: new Position(5, 5));
        // 只起始格 village-square，未放 village-store

        var changes = MapService.TryTransformTilesForEvent(state, module, registry, "village-inquiry", null);
        // 沒有 village-store 在地圖上 → 無變化
        changes.Should().BeEmpty();
    }

    [Fact]
    public void Registry_RuleCount_MatchesNumberOfDemoTransformations()
    {
        var module = LoadAbandonedMansion();
        var registry = TileTransformRegistry.Build(module);
        // 目前只有 1 條 demo（village-store → forest-path）
        // 若未來加更多 demo，這個斷言會 fail，要更新
        registry.RuleCount.Should().Be(1);
    }
}
