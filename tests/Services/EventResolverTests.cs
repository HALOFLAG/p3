using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class EventResolverTests
{
    private static EventCard Event(int tn = 10)
    {
        return new EventCard(
            Id: "test-event",
            Name: "Test Event",
            Type: EventType.Exploration,
            Tn: tn,
            Trigger: new TileEnterTrigger("warehouse"),
            Stat: Stat.Skill,
            AllowedActionTypes: new[] { ActionType.Exploration },
            Narrative: "n",
            Outcomes: new EventOutcomes(
                Success:        new EventOutcome("ok",  Array.Empty<EffectBase>()),
                PartialSuccess: new EventOutcome("mid", Array.Empty<EffectBase>()),
                Failure:        new EventOutcome("bad", Array.Empty<EffectBase>())
            ));
    }

    [Fact]
    public void Resolve_AtOrAboveTn_IsSuccess()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var dice = new FakeDiceService(new RollResult(6, 6));
        var resolver = new EventResolver(dice, new EffectHandler());

        var r = resolver.Resolve(Event(tn: 10), state, module);

        r.Tier.Should().Be(EventOutcomeTier.Success);
        state.ConsumedEventIds.Should().Contain("test-event");
    }

    [Fact]
    public void Resolve_Within3BelowTn_IsPartial()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var skill = module.Characters[state.CurrentPlayer.CharacterId].Stats.Skill;
        int tn = 10;
        int roll = tn - 2 - skill; // total = roll + skill = tn-2 → Partial
        int d1 = Math.Max(1, Math.Min(6, roll - 1));
        int d2 = roll - d1;
        var dice = new FakeDiceService(new RollResult(d1, d2));
        var resolver = new EventResolver(dice, new EffectHandler());

        var r = resolver.Resolve(Event(tn), state, module);

        r.Tier.Should().Be(EventOutcomeTier.PartialSuccess);
    }

    [Fact]
    public void Resolve_FarBelowTn_IsFailure()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var dice = new FakeDiceService(new RollResult(1, 1));
        var resolver = new EventResolver(dice, new EffectHandler());

        var r = resolver.Resolve(Event(tn: 20), state, module);

        r.Tier.Should().Be(EventOutcomeTier.Failure);
    }

    [Fact]
    public void Resolve_AppliesOutcomeEffects()
    {
        var module = ModuleFactory.Load();
        var ev = module.Events["warehouse-investigation"]; // success effects: setFlag + tileProgress
        var state = ModuleFactory.NewState(module);
        var dice = new FakeDiceService(new RollResult(6, 6));
        var resolver = new EventResolver(dice, new EffectHandler());

        resolver.Resolve(ev, state, module);

        state.Flags.Should().ContainKey("warehouse_clue_a");
    }

    // §13-4 · Success 時當前玩家 Contributions++，結局 `contributions * 2 + ...` 才會有效。
    [Fact]
    public void Resolve_Success_IncrementsCurrentPlayerContributions()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var before = state.CurrentPlayer.Contributions;
        var dice = new FakeDiceService(new RollResult(6, 6));
        var resolver = new EventResolver(dice, new EffectHandler());

        resolver.Resolve(Event(tn: 10), state, module);

        state.CurrentPlayer.Contributions.Should().Be(before + 1);
    }

    [Fact]
    public void Resolve_Partial_DoesNotIncrementContributions()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var before = state.CurrentPlayer.Contributions;
        var skill = module.Characters[state.CurrentPlayer.CharacterId].Stats.Skill;
        int tn = 10;
        int roll = tn - 2 - skill;
        int d1 = Math.Max(1, Math.Min(6, roll - 1));
        int d2 = roll - d1;
        var dice = new FakeDiceService(new RollResult(d1, d2));
        var resolver = new EventResolver(dice, new EffectHandler());

        resolver.Resolve(Event(tn), state, module);

        state.CurrentPlayer.Contributions.Should().Be(before);
    }

    [Fact]
    public void Resolve_Failure_DoesNotIncrementContributions()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var before = state.CurrentPlayer.Contributions;
        var dice = new FakeDiceService(new RollResult(1, 1));
        var resolver = new EventResolver(dice, new EffectHandler());

        resolver.Resolve(Event(tn: 20), state, module);

        state.CurrentPlayer.Contributions.Should().Be(before);
    }

    // Phase 3 任務 14（S3）· EventResolver 應同步寫 state.EventOutcomes，給 JsonLogic
    // event.<id>.outcome 條件用。三 tier 各驗一次。
    [Fact]
    public void Resolve_WritesEventOutcomesTier_OnSuccess()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var dice = new FakeDiceService(new RollResult(6, 6));
        var resolver = new EventResolver(dice, new EffectHandler());

        resolver.Resolve(Event(tn: 10), state, module);

        state.EventOutcomes.Should().ContainKey("test-event")
            .WhoseValue.Should().Be(EventOutcomeTier.Success);
    }

    [Fact]
    public void Resolve_WritesEventOutcomesTier_OnFailure()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var dice = new FakeDiceService(new RollResult(1, 1));
        var resolver = new EventResolver(dice, new EffectHandler());

        resolver.Resolve(Event(tn: 20), state, module);

        state.EventOutcomes.Should().ContainKey("test-event")
            .WhoseValue.Should().Be(EventOutcomeTier.Failure);
    }
}
