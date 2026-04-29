// OrbitProjectionTests — Phase 3 任務 14（S7）。
//
// 涵蓋：
//   - Class 投影：reveal 不通過 → C；reveal 通過 + trigger 未滿足 → B；都滿足 → A
//   - PlayerActionTrigger 永遠停在 B（靜態快照無法預測動作）
//   - HintFor 直接轉發給 OrbitHintTemplates（這裡只 sanity check 一個案例）
using System.Text.Json.Nodes;
using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Events;

public class OrbitProjectionTests
{
    private static EventCard MakeEvent(
        string id,
        EventTrigger trigger,
        string? revealConditionJson = null) => new(
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
            RevealCondition = revealConditionJson is null ? null : JsonNode.Parse(revealConditionJson),
        };

    [Fact]
    public void Refresh_RevealConditionFalse_StaysClassC()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(
            "ev",
            new TileEnterTrigger("warehouse"),
            // 條件：turn >= 5；初始 turn = 0
            """{">=":[{"var":"turn"},5]}""");
        var dict = new Dictionary<string, EventCard> { [ev.Id] = ev };
        module = module with { Events = dict };
        var state = ModuleFactory.NewState(module);

        var orbit = new EventOrbit();
        orbit.Push(new EventInstance(ev, revealCondition: ev.RevealCondition));
        var projection = new OrbitProjection(orbit, module, state);

        projection.Refresh();

        orbit.Pending[0].Class.Should().Be(EventOrbitClass.ClassC);
    }

    [Fact]
    public void Refresh_RevealPasses_TriggerNotMet_BecomesClassB()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent("ev", new TileEnterTrigger("warehouse"));
        var dict = new Dictionary<string, EventCard> { [ev.Id] = ev };
        module = module with { Events = dict };
        var state = ModuleFactory.NewState(module);
        // 玩家不在 warehouse（起始 (0,0) 為 warehouse；移開）
        state.CurrentPlayer.Position = new Position(99, 99);

        var orbit = new EventOrbit();
        orbit.Push(new EventInstance(ev));
        var projection = new OrbitProjection(orbit, module, state);

        projection.Refresh();

        orbit.Pending[0].Class.Should().Be(EventOrbitClass.ClassB);
    }

    [Fact]
    public void Refresh_RevealPasses_TriggerMet_BecomesClassA()
    {
        var module = ModuleFactory.Load();
        // valid-module 的起始 tile 是 "town-square"，玩家初始在 (0,0) → 命中此 trigger
        var ev = MakeEvent("ev", new TileEnterTrigger("town-square"));
        var dict = new Dictionary<string, EventCard> { [ev.Id] = ev };
        module = module with { Events = dict };
        var state = ModuleFactory.NewState(module);

        var orbit = new EventOrbit();
        orbit.Push(new EventInstance(ev));
        var projection = new OrbitProjection(orbit, module, state);

        projection.Refresh();

        orbit.Pending[0].Class.Should().Be(EventOrbitClass.ClassA);
    }

    [Fact]
    public void Refresh_TurnAtTrigger_ClassA_OnExactRound()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent("ev", new TurnAtTrigger(7));
        module = module with { Events = new Dictionary<string, EventCard> { [ev.Id] = ev } };
        var state = ModuleFactory.NewState(module);

        var orbit = new EventOrbit();
        orbit.Push(new EventInstance(ev));
        var projection = new OrbitProjection(orbit, module, state);

        // turn=6 → B
        state.CurrentBigRound = 6;
        projection.Refresh();
        orbit.Pending[0].Class.Should().Be(EventOrbitClass.ClassB);

        // turn=7 → A
        state.CurrentBigRound = 7;
        projection.Refresh();
        orbit.Pending[0].Class.Should().Be(EventOrbitClass.ClassA);

        // turn=8 → 又掉回 B（已過範圍）
        state.CurrentBigRound = 8;
        projection.Refresh();
        orbit.Pending[0].Class.Should().Be(EventOrbitClass.ClassB);
    }

    [Fact]
    public void Refresh_PlayerActionTrigger_AlwaysStaysB_AfterReveal()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent("ev", new PlayerActionTrigger(PlayerActionKind.Rest, null));
        module = module with { Events = new Dictionary<string, EventCard> { [ev.Id] = ev } };
        var state = ModuleFactory.NewState(module);

        var orbit = new EventOrbit();
        orbit.Push(new EventInstance(ev));
        var projection = new OrbitProjection(orbit, module, state);

        projection.Refresh();

        // PlayerAction 為事件流，靜態快照永遠停在 B（已揭露但等動作）。
        orbit.Pending[0].Class.Should().Be(EventOrbitClass.ClassB);
    }

    [Fact]
    public void HintFor_DelegatesToTemplates()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent("ev", new TurnAtTrigger(7));
        var state = ModuleFactory.NewState(module);
        var orbit = new EventOrbit();
        var projection = new OrbitProjection(orbit, module, state);

        projection.HintFor(ev).Should().Contain("第 7 回合");
    }
}
