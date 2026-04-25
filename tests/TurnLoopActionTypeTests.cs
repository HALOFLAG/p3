using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests;

public class TurnLoopActionTypeTests
{
    private static (TurnLoop loop, Module module) Setup(string tileId)
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(0, 0)] = new PlacedTile { TileId = tileId, Level = ExplorationLevel.Unfamiliar };
        state.CurrentPlayer.Position = new Position(0, 0);
        state.Phase = TurnPhase.Action;
        state.CurrentPlayer.ActionPoints = 3;
        state.CurrentPlayer.Hand.Clear();
        state.CurrentPlayer.Hand.AddRange(module.ActionCards.Keys);
        return (new TurnLoop(state, new SeededDiceService(1), module), module);
    }

    [Fact]
    public void PlayCard_RejectedWhenTypeNotAllowedOnTile()
    {
        // wilderness-path allows only Exploration+Thinking; basic-attack is Combat.
        var (loop, _) = Setup("wilderness-path");
        var act = () => loop.PlayCard("basic-attack", Stat.Power, 8);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PlayCard_AcceptedWhenTypeAllowed()
    {
        var (loop, _) = Setup("wilderness-path");
        // careful-search is Exploration
        var act = () => loop.PlayCard("careful-search", Stat.Skill, 8);
        act.Should().NotThrow();
    }

    [Fact]
    public void PlayCard_PromotesTileOncePerVisit()
    {
        var (loop, _) = Setup("wilderness-path");
        loop.State.TileMap[(0, 0)].Level = ExplorationLevel.Unfamiliar;

        loop.PlayCard("careful-search", Stat.Skill, 8);
        loop.State.TileMap[(0, 0)].Level.Should().Be(ExplorationLevel.Neutral);

        // Second card (if cost permits) doesn't re-promote
        loop.State.CurrentPlayer.Hand.Add("careful-search");
        loop.PlayCard("careful-search", Stat.Skill, 8);
        loop.State.TileMap[(0, 0)].Level.Should().Be(ExplorationLevel.Neutral);
    }

    [Fact]
    public void PlayCard_MovesCardToDiscard()
    {
        var (loop, _) = Setup("wilderness-path");
        loop.PlayCard("careful-search", Stat.Skill, 8);
        loop.State.CurrentPlayer.Hand.Should().NotContain("careful-search");
        loop.State.CurrentPlayer.Discard.Should().Contain("careful-search");
    }
}
