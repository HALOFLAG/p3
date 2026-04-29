// JsonLogicContextBuilderTests — 驗證從 GameState/Module/Orbit 組出 JsonLogic 變數命名空間。
// 規格 §5.3 8 類變數，本測試覆蓋 turn / hero.* / companion.* / currentTile.* / orbit.*。
using System.Text.Json;
using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class JsonLogicContextBuilderTests
{
    private static EventCard MakeEvent(string id) => new(
        Id: id,
        Name: id,
        Type: EventType.Exploration,
        Tn: 8,
        Trigger: new TileEnterTrigger("any"),
        Stat: Stat.Power,
        AllowedActionTypes: Array.Empty<ActionType>(),
        Narrative: "",
        Outcomes: new EventOutcomes(
            new EventOutcome("", Array.Empty<EffectBase>()),
            new EventOutcome("", Array.Empty<EffectBase>()),
            new EventOutcome("", Array.Empty<EffectBase>())));

    [Fact]
    public void BuildsCoreVariablesFromGameState()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.CurrentBigRound = 7;
        state.Flags["plot_started"] = JsonDocument.Parse("true").RootElement;

        var ctx = JsonLogicContextBuilder.FromGameState(state, module);

        ctx.Variables.Should().ContainKey("turn");
        ctx.Variables["turn"]!.GetValue<int>().Should().Be(7);

        ctx.Variables.Should().ContainKey("flag.plot_started");
        ctx.Variables["flag.plot_started"]!.GetValue<bool>().Should().BeTrue();

        ctx.Variables.Should().ContainKey("hero.hp");
        ctx.Variables.Should().ContainKey("hero.ap");
        ctx.Variables.Should().ContainKey("hero.attr.power");

        ctx.Variables.Should().ContainKey("companion.count");
        ctx.Variables["companion.count"]!.GetValue<int>().Should().Be(state.Companions.Count);

        ctx.Variables.Should().ContainKey("currentTile.row");
        ctx.Variables.Should().ContainKey("currentTile.col");
        ctx.Variables.Should().ContainKey("currentTile.tileCardId");
        ctx.Variables.Should().ContainKey("currentTile.terrain");
    }

    [Fact]
    public void OrbitContainsUsesEventIdSuffix()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var orbit = new EventOrbit();
        orbit.Push(new EventInstance(MakeEvent("evt-foo")));
        orbit.Push(new EventInstance(MakeEvent("evt-bar")));

        var ctx = JsonLogicContextBuilder.FromGameState(state, module, orbit);

        ctx.Variables.Should().ContainKey("orbit.contains.evt-foo");
        ctx.Variables["orbit.contains.evt-foo"]!.GetValue<bool>().Should().BeTrue();
        ctx.Variables.Should().ContainKey("orbit.contains.evt-bar");
        ctx.Variables.Should().ContainKey("orbit.A.count");
        ctx.Variables.Should().ContainKey("orbit.B.count");
        ctx.Variables.Should().ContainKey("orbit.C.count");
        // Push 預設為 ClassC（規格 §1.6）
        ctx.Variables["orbit.C.count"]!.GetValue<int>().Should().Be(2);
        ctx.Variables["orbit.A.count"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void ResolvesOrbitContainsViaJsonLogic()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var orbit = new EventOrbit();
        orbit.Push(new EventInstance(MakeEvent("evt-foo")));

        var ctx = JsonLogicContextBuilder.FromGameState(state, module, orbit);
        var ev = new JsonLogicEvaluator();

        // {"var": "orbit.contains.evt-foo"} 在 toData 後可正確取到 true
        var rule = System.Text.Json.Nodes.JsonNode.Parse("""{"var":"orbit.contains.evt-foo"}""");
        ev.Evaluate(rule, ctx).Should().BeTrue();

        // 不在軌道上的事件 id → 變數缺失 → 安全降級為 false
        var ruleMiss = System.Text.Json.Nodes.JsonNode.Parse("""{"var":"orbit.contains.evt-not-there"}""");
        ev.Evaluate(ruleMiss, ctx).Should().BeFalse();
    }

    [Fact]
    public void TileLazyResolverExposesEachPlacedTile()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        // CreateNew 已置起始地塊在 (0,0)；確認其 var 路徑可被 JsonLogic 取到
        var startTileId = state.TileMap[(0, 0)].TileId;

        var ctx = JsonLogicContextBuilder.FromGameState(state, module);

        // 起始地塊：tile.0.0.tileCardId == 起始 id
        ctx.Variables.Should().ContainKey("tile.0.0.tileCardId");
        ctx.Variables["tile.0.0.tileCardId"]!.GetValue<string>().Should().Be(startTileId);
        ctx.Variables.Should().ContainKey("tile.0.0.terrain");
        ctx.Variables.Should().ContainKey("tile.0.0.isImportant");
        ctx.Variables.Should().ContainKey("tile.0.0.level");

        // 透過 JsonLogic 取值能命中
        var ev = new JsonLogicEvaluator();
        var rule = System.Text.Json.Nodes.JsonNode.Parse(
            $$"""{"==":[{"var":"tile.0.0.tileCardId"},"{{startTileId}}"]}""");
        ev.Evaluate(rule, ctx).Should().BeTrue();
    }

    [Fact]
    public void OmitsHeroSectionWhenNoPlayers()
    {
        var module = ModuleFactory.Load();
        var state = new GameState { RngSeed = 1, MaxBigRounds = 30 };

        var ctx = JsonLogicContextBuilder.FromGameState(state, module);

        ctx.Variables.Should().ContainKey("turn");
        ctx.Variables.Should().NotContainKey("hero.hp");
        ctx.Variables.Should().NotContainKey("currentTile.row");
        ctx.Variables["companion.count"]!.GetValue<int>().Should().Be(0);
    }

    // ─── Phase 3 任務 14（S3）擴充變數 ────────────────────────

    [Fact]
    public void HeroHpMaxAndRatioExposed()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        // 強制 HP = 半血以驗 ratio
        state.CurrentPlayer.Hp = state.CurrentPlayer.HpMax / 2;

        var ctx = JsonLogicContextBuilder.FromGameState(state, module);

        ctx.Variables.Should().ContainKey("hero.hpMax");
        ctx.Variables["hero.hpMax"]!.GetValue<int>().Should().Be(state.CurrentPlayer.HpMax);
        ctx.Variables.Should().ContainKey("hero.hpRatio");
        ctx.Variables["hero.hpRatio"]!.GetValue<double>().Should().BeApproximately(0.5, 0.05);
    }

    [Fact]
    public void EventConsumedAndOutcomeExposed()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.ConsumedEventIds.Add("warehouse-investigation");
        state.EventOutcomes["warehouse-investigation"] = EventOutcomeTier.PartialSuccess;

        var ctx = JsonLogicContextBuilder.FromGameState(state, module);

        ctx.Variables["event.warehouse-investigation.consumed"]!.GetValue<bool>().Should().BeTrue();
        ctx.Variables["event.warehouse-investigation.outcome"]!.GetValue<string>().Should().Be("partialSuccess");
    }

    [Fact]
    public void HasEquipmentExposed_FromBackpackAndEquipped()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.CurrentPlayer.Backpack.Add("torch");
        state.CurrentPlayer.Equipment[EquipmentSlot.Hand] = "rope";

        var ctx = JsonLogicContextBuilder.FromGameState(state, module);

        ctx.Variables["hasEquipment.torch"]!.GetValue<bool>().Should().BeTrue();
        ctx.Variables["hasEquipment.rope"]!.GetValue<bool>().Should().BeTrue();
        ctx.Variables.Should().NotContainKey("hasEquipment.nonexistent");
    }

    [Fact]
    public void HasIntelExposed()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.AcquiredIntel.Add("underground-passage");

        var ctx = JsonLogicContextBuilder.FromGameState(state, module);

        ctx.Variables["hasIntel.underground-passage"]!.GetValue<bool>().Should().BeTrue();
        ctx.Variables.Should().NotContainKey("hasIntel.nonexistent");
    }

    [Fact]
    public void TilePlacedById_IsTrueForStartingTile()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var startTileId = state.TileMap[(0, 0)].TileId;

        var ctx = JsonLogicContextBuilder.FromGameState(state, module);

        ctx.Variables[$"tilePlaced.{startTileId}"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void ActionCountsExposed_LowercaseFirstLetter()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.ActionCounts[PlayerActionKind.Move] = 3;
        state.ActionCounts[PlayerActionKind.Observe] = 2;

        var ctx = JsonLogicContextBuilder.FromGameState(state, module);

        ctx.Variables["action.move.count"]!.GetValue<int>().Should().Be(3);
        ctx.Variables["action.observe.count"]!.GetValue<int>().Should().Be(2);
        ctx.Variables.Should().NotContainKey("action.rest.count");
    }
}
