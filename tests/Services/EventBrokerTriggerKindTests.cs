// EventBroker S2 + S3 trigger / reveal 測試 — 涵蓋 TurnAt / TurnRange / PlayerAction 三類新觸發
// 與 RevealCondition gating（S3）。
//
// 與 EventBrokerTests（S1 tileEnter / 多事件命中）互補；
// 共同 Sink 與合成 module 模式。
using System.Text.Json.Nodes;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class EventBrokerTriggerKindTests
{
    private sealed class CapturingSink : IEventBrokerSink
    {
        public List<string> Triggered { get; } = new();
        public void OnEventTriggered(EventCard card) => Triggered.Add(card.Id);
    }

    private static Module BuildModuleWith(params (string Id, EventTrigger Trigger)[] events)
    {
        var baseModule = ModuleFactory.Load();
        var dict = events.ToDictionary(
            e => e.Id,
            e => new EventCard(
                Id: e.Id,
                Name: e.Id,
                Type: EventType.Exploration,
                Tn: 8,
                Trigger: e.Trigger,
                Stat: Stat.Skill,
                AllowedActionTypes: System.Array.Empty<ActionType>(),
                Narrative: "n",
                Outcomes: new EventOutcomes(
                    Success: new EventOutcome("s", System.Array.Empty<EffectBase>()),
                    PartialSuccess: new EventOutcome("p", System.Array.Empty<EffectBase>()),
                    Failure: new EventOutcome("f", System.Array.Empty<EffectBase>()))));
        return baseModule with { Events = dict };
    }

    private static EventCard MakeEventWithReveal(string id, EventTrigger trigger, string revealJson)
    {
        return new EventCard(
            Id: id,
            Name: id,
            Type: EventType.Exploration,
            Tn: 8,
            Trigger: trigger,
            Stat: Stat.Skill,
            AllowedActionTypes: System.Array.Empty<ActionType>(),
            Narrative: "n",
            Outcomes: new EventOutcomes(
                Success: new EventOutcome("s", System.Array.Empty<EffectBase>()),
                PartialSuccess: new EventOutcome("p", System.Array.Empty<EffectBase>()),
                Failure: new EventOutcome("f", System.Array.Empty<EffectBase>())))
        {
            RevealCondition = JsonNode.Parse(revealJson),
        };
    }

    // ─── TurnAtTrigger ──────────────────────────────────────────

    [Fact]
    public void OnActionPhaseBegin_TurnAt_FiresOnExactRound()
    {
        var module = BuildModuleWith(("ev-turn5", new TurnAtTrigger(5)));
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnActionPhaseBegin(4);
        broker.OnActionPhaseBegin(5);
        broker.OnActionPhaseBegin(6);

        sink.Triggered.Should().ContainSingle().Which.Should().Be("ev-turn5");
    }

    // ─── TurnRangeTrigger ───────────────────────────────────────

    [Fact]
    public void OnActionPhaseBegin_TurnRange_FiresInsideRange()
    {
        var module = BuildModuleWith(("ev-range", new TurnRangeTrigger(3, 5)));
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnActionPhaseBegin(2);
        sink.Triggered.Should().BeEmpty();

        broker.OnActionPhaseBegin(3);
        sink.Triggered.Should().ContainSingle();

        // 已消費 → 同事件不再 fire
        state.ConsumedEventIds.Add("ev-range");
        sink.Triggered.Clear();
        broker.OnActionPhaseBegin(4);
        broker.OnActionPhaseBegin(5);
        broker.OnActionPhaseBegin(6);
        sink.Triggered.Should().BeEmpty();
    }

    [Fact]
    public void OnActionPhaseBegin_TurnRange_OpenEnd_FiresFromOnwards()
    {
        var module = BuildModuleWith(("ev-from10", new TurnRangeTrigger(10, null)));
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnActionPhaseBegin(9);
        sink.Triggered.Should().BeEmpty();

        broker.OnActionPhaseBegin(10);
        sink.Triggered.Should().ContainSingle();
    }

    // ─── PlayerActionTrigger ────────────────────────────────────

    [Fact]
    public void OnPlayerAction_KindOnly_FiresEveryTime()
    {
        var module = BuildModuleWith(("ev-rest", new PlayerActionTrigger(PlayerActionKind.Rest, null)));
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnPlayerAction(PlayerActionKind.Move);  // 不命中
        broker.OnPlayerAction(PlayerActionKind.Rest);  // 命中 → 但事件未消費（測試 sink 不會自動加 ConsumedEventIds）
        broker.OnPlayerAction(PlayerActionKind.Rest);  // 仍命中

        sink.Triggered.Should().HaveCount(2);
        sink.Triggered.Should().AllBe("ev-rest");
    }

    [Fact]
    public void OnPlayerAction_WithCount_FiresOnNthTime()
    {
        var module = BuildModuleWith(("ev-obs3", new PlayerActionTrigger(PlayerActionKind.Observe, 3)));
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnPlayerAction(PlayerActionKind.Observe); // count=1
        broker.OnPlayerAction(PlayerActionKind.Observe); // count=2
        sink.Triggered.Should().BeEmpty();

        broker.OnPlayerAction(PlayerActionKind.Observe); // count=3 → fire
        sink.Triggered.Should().ContainSingle().Which.Should().Be("ev-obs3");
    }

    [Fact]
    public void OnPlayerAction_IncrementsActionCounts()
    {
        var module = BuildModuleWith(); // no events
        var state = ModuleFactory.NewState(module);
        var broker = new EventBroker(state, module);

        broker.OnPlayerAction(PlayerActionKind.Move);
        broker.OnPlayerAction(PlayerActionKind.Move);
        broker.OnPlayerAction(PlayerActionKind.Rest);

        state.ActionCounts.Should().ContainKey(PlayerActionKind.Move).WhoseValue.Should().Be(2);
        state.ActionCounts.Should().ContainKey(PlayerActionKind.Rest).WhoseValue.Should().Be(1);
        state.ActionCounts.Should().NotContainKey(PlayerActionKind.Observe);
    }

    // ─── RevealCondition gating（S3）───────────────────────────

    [Fact]
    public void OnTileEnter_RevealConditionFalse_DoesNotFire()
    {
        var baseModule = ModuleFactory.Load();
        var ev = MakeEventWithReveal(
            "ev-gated",
            new TileEnterTrigger("warehouse"),
            // 條件：需要 turn >= 5；初始 turn = 0
            """{">=":[{"var":"turn"},5]}""");
        var module = baseModule with { Events = new Dictionary<string, EventCard> { [ev.Id] = ev } };
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnTileEnter("warehouse");
        sink.Triggered.Should().BeEmpty();
    }

    [Fact]
    public void OnTileEnter_RevealConditionTrue_Fires()
    {
        var baseModule = ModuleFactory.Load();
        var ev = MakeEventWithReveal(
            "ev-gated",
            new TileEnterTrigger("warehouse"),
            """{">=":[{"var":"turn"},5]}""");
        var module = baseModule with { Events = new Dictionary<string, EventCard> { [ev.Id] = ev } };
        var state = ModuleFactory.NewState(module);
        state.CurrentBigRound = 5;
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnTileEnter("warehouse");
        sink.Triggered.Should().ContainSingle().Which.Should().Be("ev-gated");
    }

    [Fact]
    public void RevealCondition_HasIntel_GatingWorks()
    {
        var baseModule = ModuleFactory.Load();
        var ev = MakeEventWithReveal(
            "ev-needs-intel",
            new TileEnterTrigger("warehouse"),
            """{"var":"hasIntel.priest-diary"}""");
        var module = baseModule with { Events = new Dictionary<string, EventCard> { [ev.Id] = ev } };
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        // 沒拿情報 → 不觸發
        broker.OnTileEnter("warehouse");
        sink.Triggered.Should().BeEmpty();

        // 拿到情報 → 觸發
        state.AcquiredIntel.Add("priest-diary");
        broker.OnTileEnter("warehouse");
        sink.Triggered.Should().ContainSingle();
    }

    [Fact]
    public void RevealCondition_EventOutcome_GatingWorks()
    {
        var baseModule = ModuleFactory.Load();
        var ev = MakeEventWithReveal(
            "ev-after-foo",
            new TileEnterTrigger("warehouse"),
            """{"==":[{"var":"event.foo.outcome"},"success"]}""");
        var module = baseModule with { Events = new Dictionary<string, EventCard> { [ev.Id] = ev } };
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        // 前置事件 foo 還沒結算 → 不觸發
        broker.OnTileEnter("warehouse");
        sink.Triggered.Should().BeEmpty();

        // foo 失敗 → 仍不觸發
        state.EventOutcomes["foo"] = EventOutcomeTier.Failure;
        broker.OnTileEnter("warehouse");
        sink.Triggered.Should().BeEmpty();

        // foo 成功 → 觸發
        state.EventOutcomes["foo"] = EventOutcomeTier.Success;
        broker.OnTileEnter("warehouse");
        sink.Triggered.Should().ContainSingle();
    }

    // ─── Obsolete trigger fallback ──────────────────────────────

    [Fact]
    public void ObsoleteTriggers_AreIgnoredAtRuntime()
    {
#pragma warning disable CS0618 // 測試 obsolete trigger 故意實例化
        var module = BuildModuleWith(
            ("ev-old-timer", new TurnTimerTrigger(5)),
            ("ev-old-count", new ActionCountTrigger(3)));
#pragma warning restore CS0618
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnActionPhaseBegin(5);
        broker.OnActionPhaseBegin(10);
        broker.OnPlayerAction(PlayerActionKind.Move);

        sink.Triggered.Should().BeEmpty();
    }
}
