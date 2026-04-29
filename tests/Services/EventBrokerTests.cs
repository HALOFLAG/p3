// EventBroker S1 單元測試 — tileEnter 觸發路徑（Phase 3 任務 14）。
//
// 涵蓋：
//   - OnTileEnter 命中現存 tileEnter 事件 → Sink.OnEventTriggered 被呼叫
//   - 未命中時不呼叫 Sink
//   - ConsumedEventIds 過濾既已消費事件
//   - 多事件命中時第一張 → Sink，其餘進 PendingEventQueue
//   - 沒有 Sink 也不會 throw
//
// S2+ 會新增 turnAt / turnRange / playerAction trigger，屆時補測試到 EventBrokerTriggerKindTests。
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class EventBrokerTests
{
    private sealed class CapturingSink : IEventBrokerSink
    {
        public List<string> Triggered { get; } = new();
        public void OnEventTriggered(EventCard card) => Triggered.Add(card.Id);
    }

    [Fact]
    public void OnTileEnter_Matches_TileEnterTrigger_AndCallsSink()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnTileEnter("warehouse");

        sink.Triggered.Should().ContainSingle().Which.Should().Be("warehouse-investigation");
    }

    [Fact]
    public void OnTileEnter_SkipsConsumedEvents()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.ConsumedEventIds.Add("warehouse-investigation");
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnTileEnter("warehouse");

        sink.Triggered.Should().BeEmpty();
    }

    [Fact]
    public void OnTileEnter_NoMatch_DoesNotCallSink()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnTileEnter("nonexistent-tile-id");

        sink.Triggered.Should().BeEmpty();
    }

    [Fact]
    public void OnTileEnter_NoSink_DoesNotThrow()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var broker = new EventBroker(state, module); // sink intentionally null

        var act = () => broker.OnTileEnter("warehouse");
        act.Should().NotThrow();
    }

    [Fact]
    public void OnTileEnter_MultipleHits_FiresFirst_QueuesRest()
    {
        // 構造一個有兩張 tileEnter 同 tileId 的合成 module。
        var baseModule = ModuleFactory.Load();
        var state = ModuleFactory.NewState(baseModule);

        var ev1 = new EventCard(
            Id: "test-evA",
            Name: "Synthetic A",
            Type: EventType.Exploration,
            Tn: 8,
            Trigger: new TileEnterTrigger("warehouse"),
            Stat: Stat.Skill,
            AllowedActionTypes: System.Array.Empty<ActionType>(),
            Narrative: "A",
            Outcomes: new EventOutcomes(
                Success: new EventOutcome("ok", System.Array.Empty<EffectBase>()),
                PartialSuccess: new EventOutcome("p", System.Array.Empty<EffectBase>()),
                Failure: new EventOutcome("f", System.Array.Empty<EffectBase>())));
        var ev2 = ev1 with { Id = "test-evB", Name = "Synthetic B" };

        var events = new Dictionary<string, EventCard>
        {
            [ev1.Id] = ev1,
            [ev2.Id] = ev2,
        };
        var module = baseModule with { Events = events };

        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnTileEnter("warehouse");

        sink.Triggered.Should().ContainSingle().Which.Should().Be("test-evA");
        state.PendingEventQueue.Should().Contain("test-evB");
    }

    // ─── S5: Prologue.EventPriorities 多事件命中排序 ─────────

    [Fact]
    public void OnTileEnter_MultipleHits_HonorsPrologueEventPriorities()
    {
        // 構造：兩張 tileEnter 同 tileId 的事件 ev-low / ev-high；
        // module 載入序：ev-low 先；prologue.eventPriorities 把 ev-high 排前。
        // 期望：sink 收到 ev-high（不是載入序的 ev-low）。
        var baseModule = ModuleFactory.Load();
        var ev1 = new EventCard(
            Id: "ev-low",
            Name: "low",
            Type: EventType.Exploration,
            Tn: 8,
            Trigger: new TileEnterTrigger("warehouse"),
            Stat: Stat.Skill,
            AllowedActionTypes: System.Array.Empty<ActionType>(),
            Narrative: "n",
            Outcomes: new EventOutcomes(
                Success: new EventOutcome("s", System.Array.Empty<EffectBase>()),
                PartialSuccess: new EventOutcome("p", System.Array.Empty<EffectBase>()),
                Failure: new EventOutcome("f", System.Array.Empty<EffectBase>())));
        var ev2 = ev1 with { Id = "ev-high", Name = "high" };

        // 載入序刻意 ev-low 先
        var events = new Dictionary<string, EventCard>
        {
            [ev1.Id] = ev1,
            [ev2.Id] = ev2,
        };
        var prologue = baseModule.Prologue with { EventPriorities = new[] { "ev-high" } };
        var module = baseModule with { Events = events, Prologue = prologue };

        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnTileEnter("warehouse");

        // 第一張 sink 收到 ev-high；ev-low 進 PendingEventQueue。
        sink.Triggered.Should().ContainSingle().Which.Should().Be("ev-high");
        state.PendingEventQueue.Should().ContainSingle().Which.Should().Be("ev-low");
    }

    [Fact]
    public void OnTileEnter_PartialPriorities_FallbacksToModuleOrder()
    {
        // 三張命中：A / B / C；EventPriorities 只列 ["B"]。
        // 期望：B 第一、A / C 依 module 載入序在 B 之後。
        var baseModule = ModuleFactory.Load();
        EventCard Make(string id) => new(
            Id: id,
            Name: id,
            Type: EventType.Exploration,
            Tn: 8,
            Trigger: new TileEnterTrigger("warehouse"),
            Stat: Stat.Skill,
            AllowedActionTypes: System.Array.Empty<ActionType>(),
            Narrative: "n",
            Outcomes: new EventOutcomes(
                Success: new EventOutcome("s", System.Array.Empty<EffectBase>()),
                PartialSuccess: new EventOutcome("p", System.Array.Empty<EffectBase>()),
                Failure: new EventOutcome("f", System.Array.Empty<EffectBase>())));

        var events = new Dictionary<string, EventCard>
        {
            ["A"] = Make("A"),
            ["B"] = Make("B"),
            ["C"] = Make("C"),
        };
        var prologue = baseModule.Prologue with { EventPriorities = new[] { "B" } };
        var module = baseModule with { Events = events, Prologue = prologue };

        var state = ModuleFactory.NewState(module);
        var sink = new CapturingSink();
        var broker = new EventBroker(state, module) { Sink = sink };

        broker.OnTileEnter("warehouse");

        sink.Triggered.Should().ContainSingle().Which.Should().Be("B");
        // 順序：B 已 sink；queue 中按 module 載入序 A → C
        state.PendingEventQueue.Should().Equal("A", "C");
    }
}
