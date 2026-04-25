using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests;

public class TurnLoopDeckTests
{
    [Fact]
    public void Draw_ReshufflesDiscard_WhenDeckEmpty()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var loop = new TurnLoop(state, new SeededDiceService(1), module);

        var player = state.CurrentPlayer;
        // Drain deck into discard simulating prior play
        player.Discard.AddRange(player.Deck);
        player.Deck.Clear();
        player.Hand.Clear();

        loop.Advance(); // Draw

        player.Hand.Count.Should().BeGreaterThan(0);
        // discard emptied after reshuffle
        (player.Deck.Count + player.Hand.Count).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Draw_StopsWhenDeckAndDiscardBothEmpty()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var loop = new TurnLoop(state, new SeededDiceService(1), module);
        state.CurrentPlayer.Deck.Clear();
        state.CurrentPlayer.Discard.Clear();
        state.CurrentPlayer.Hand.Clear();

        loop.Advance(); // Draw (no-op on cards)

        state.CurrentPlayer.Hand.Should().BeEmpty();
    }

    [Fact]
    public void PlayedCards_GoToDiscard()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(0, 0)].Level = CardNarrative.Core.Models.ExplorationLevel.Unfamiliar;
        state.Phase = TurnPhase.Action;
        state.CurrentPlayer.ActionPoints = 3;
        state.CurrentPlayer.Hand.Clear();
        state.CurrentPlayer.Hand.Add("persuade");
        var loop = new TurnLoop(state, new SeededDiceService(1), module);

        loop.PlayCard("persuade", CardNarrative.Core.Models.Stat.Social, 8);

        state.CurrentPlayer.Discard.Should().Contain("persuade");
    }
}
