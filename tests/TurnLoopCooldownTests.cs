using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests;

public class TurnLoopCooldownTests
{
    private static (TurnLoop loop, GameState state, Module module) Setup()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        // Place the current player on town-square (allows thinking + communication)
        state.TileMap[(0, 0)] = new PlacedTile { TileId = "town-square", Level = ExplorationLevel.Unfamiliar };
        state.CurrentPlayer.Position = new Position(0, 0);
        state.Phase = TurnPhase.Action;
        state.CurrentPlayer.ActionPoints = 3;
        state.CurrentPlayer.Hand.Clear();
        state.CurrentPlayer.Hand.AddRange(new[] { "integrate-intel", "integrate-intel", "persuade" });
        var loop = new TurnLoop(state, new FakeDiceService(new RollResult(3, 3)), module);
        return (loop, state, module);
    }

    [Fact]
    public void PlayCard_PerTurnLimit_BlocksSecondUse()
    {
        var (loop, state, _) = Setup();
        loop.PlayCard("integrate-intel", Stat.Intellect, 8);

        Action act = () => loop.PlayCard("integrate-intel", Stat.Intellect, 8);

        act.Should().Throw<InvalidOperationException>().WithMessage("*per-turn limit*");
        state.CurrentPlayer.ActionCardUsesThisTurn["integrate-intel"].Should().Be(1);
    }

    [Fact]
    public void PerTurnCounter_ResetsOnDraw()
    {
        var (loop, state, _) = Setup();
        loop.PlayCard("integrate-intel", Stat.Intellect, 8);
        state.CurrentPlayer.ActionCardUsesThisTurn.Should().ContainKey("integrate-intel");

        // Force next turn: go through TurnEnd and Draw
        state.Phase = TurnPhase.TurnEnd;
        loop.Advance(); // to Draw
        loop.Advance(); // Draw runs

        state.CurrentPlayer.ActionCardUsesThisTurn.Should().BeEmpty();
    }

    [Fact]
    public void PlayCard_PerEventLimit_Shared_AcrossPlayers()
    {
        // "investigate" is exploration; use warehouse tile which allows it
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(0, 0)] = new PlacedTile { TileId = "warehouse", Level = ExplorationLevel.Unfamiliar };
        state.CurrentPlayer.Position = new Position(0, 0);
        state.Phase = TurnPhase.Action;
        state.CurrentPlayer.ActionPoints = 3;
        state.CurrentPlayer.Hand.Clear();
        state.CurrentPlayer.Hand.Add("investigate");
        state.ActionCardUsesThisEvent["investigate"] = 2; // party already at cap
        var loop = new TurnLoop(state, new FakeDiceService(new RollResult(3, 3)), module);

        Action act = () => loop.PlayCard("investigate", Stat.Skill, 8);

        act.Should().Throw<InvalidOperationException>().WithMessage("*per-event limit*");
    }

    [Fact]
    public void PerEventCounter_ClearsOnNewEvent()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        // Place warehouse adjacent to starting tile; player moves onto it to trigger onEnter.
        state.TileMap[(1, 0)] = new PlacedTile { TileId = "warehouse", Level = ExplorationLevel.Unfamiliar };
        state.ActionCardUsesThisEvent["investigate"] = 2;
        state.Phase = TurnPhase.Action;
        var loop = new TurnLoop(state, new FakeDiceService(new RollResult(3, 3)), module, new EventScheduler());

        loop.Move(new Position(1, 0)); // enters warehouse → triggers warehouse-investigation event

        state.ActionCardUsesThisEvent.Should().NotContainKey("investigate");
        loop.PendingEvent.Should().NotBeNull();
    }
}
