using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class CompanionApSharingTests
{
    [Fact]
    public void DoDraw_PlayerWithBoundCompanion_GainsExtraActionPoints()
    {
        var module = ModuleFactory.Load();
        var chars = module.Characters.Keys.Take(1).ToList();
        var comps = new[] { "companion-thief" };
        var bindings = new Dictionary<string, string> { ["companion-thief"] = chars[0] };
        var state = GameState.CreateNew(module, chars, comps, seed: 1, bindings);
        var dice = new SeededDiceService(1);
        var loop = new TurnLoop(state, dice, module);

        loop.Advance(); // Draw

        var expected = TurnLoop.ActionPointsPerTurn + module.NpcCompanions["companion-thief"].ActionPoints;
        state.CurrentPlayer.ActionPoints.Should().Be(expected);
    }

    [Fact]
    public void DoDraw_PlayerWithoutBinding_OnlyGetsBaseAp()
    {
        var module = ModuleFactory.Load();
        var chars = module.Characters.Keys.Take(1).ToList();
        var state = GameState.CreateNew(module, chars, Array.Empty<string>(), seed: 1);
        var dice = new SeededDiceService(1);
        var loop = new TurnLoop(state, dice, module);

        loop.Advance();

        state.CurrentPlayer.ActionPoints.Should().Be(TurnLoop.ActionPointsPerTurn);
    }

    [Fact]
    public void CreateNew_WithBindings_SetsBoundCompanionId()
    {
        var module = ModuleFactory.Load();
        var chars = module.Characters.Keys.Take(2).ToList();
        var bindings = new Dictionary<string, string> { ["companion-thief"] = chars[1] };
        var state = GameState.CreateNew(module, chars, new[] { "companion-thief" }, seed: 1, bindings);

        state.Players[0].BoundCompanionId.Should().BeNull();
        state.Players[1].BoundCompanionId.Should().Be("companion-thief");
    }

    [Fact]
    public void CreateNew_BindingToUnknownCharacter_IsIgnored()
    {
        var module = ModuleFactory.Load();
        var chars = module.Characters.Keys.Take(1).ToList();
        var bindings = new Dictionary<string, string> { ["companion-thief"] = "nonexistent-char" };
        var state = GameState.CreateNew(module, chars, new[] { "companion-thief" }, seed: 1, bindings);

        state.Players.Should().OnlyContain(p => p.BoundCompanionId == null);
    }
}
