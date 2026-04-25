using System.Text.Json;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class EffectHandlerTests
{
    [Fact]
    public void SetFlag_WritesToGameState()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var handler = new EffectHandler();

        handler.Apply(new SetFlagEffect("clue_a", JsonDocument.Parse("true").RootElement), state);

        state.Flags.Should().ContainKey("clue_a");
        state.Flags["clue_a"].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void GrantResource_AccumulatesAmount()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var handler = new EffectHandler();

        handler.Apply(new GrantResourceEffect("clue", 2), state);
        handler.Apply(new GrantResourceEffect("clue", 3), state);

        state.Resources["clue"].Should().Be(5);
    }

    [Fact]
    public void ClimaxTnIncrease_AddsToBonus()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var handler = new EffectHandler();

        handler.Apply(new ClimaxTnIncreaseEffect(2), state);
        handler.Apply(new ClimaxTnIncreaseEffect(1), state);

        state.ClimaxTnBonus.Should().Be(3);
    }

    [Fact]
    public void TriggerBattle_WritesPendingBattleId()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var handler = new EffectHandler();

        handler.Apply(new TriggerBattleEffect("warehouse-guard"), state);

        state.PendingBattleId.Should().Be("warehouse-guard");
    }
}
