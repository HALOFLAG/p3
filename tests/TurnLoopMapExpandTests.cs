using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests;

public class TurnLoopMapExpandTests
{
    private static TurnLoop MakeLoop()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        return new TurnLoop(state, new SeededDiceService(1), module);
    }

    [Fact]
    public void Advance_StopsAtMapExpand_UntilPlaceOrSkipCalled()
    {
        var loop = MakeLoop();
        loop.State.Phase = TurnPhase.EventCheck;
        loop.Advance(); // EventCheck → MapExpand
        loop.State.Phase.Should().Be(TurnPhase.MapExpand);

        loop.Advance(); // No-op because deck non-empty & no place/skip
        loop.State.Phase.Should().Be(TurnPhase.MapExpand);
    }

    [Fact]
    public void Advance_AutoChains_EmptyMapExpand_ToNextTurn()
    {
        var loop = MakeLoop();
        loop.State.TileDeck.Clear();
        loop.State.Phase = TurnPhase.MapExpand;
        int playerBefore = loop.State.CurrentPlayerIndex;

        loop.Advance(); // MapExpand(empty) → TurnEnd → next turn Draw → Action

        loop.State.Phase.Should().Be(TurnPhase.Action);
        loop.State.CurrentPlayerIndex.Should().NotBe(playerBefore);
    }

    [Fact]
    public void PlaceTile_AutoChains_ToNextTurn()
    {
        var loop = MakeLoop();
        loop.State.Phase = TurnPhase.MapExpand;
        var tid = loop.State.TileDeck[0];
        var cells = loop.GetValidPlacementCells();
        cells.Should().NotBeEmpty();
        int playerBefore = loop.State.CurrentPlayerIndex;

        loop.PlaceTile(tid, cells[0].X, cells[0].Y);

        loop.State.Phase.Should().Be(TurnPhase.Action);
        loop.State.CurrentPlayerIndex.Should().NotBe(playerBefore);
        loop.State.TileMap.Should().ContainKey((cells[0].X, cells[0].Y));
    }

    [Fact]
    public void SkipPlacement_AutoChains_ToNextTurn()
    {
        var loop = MakeLoop();
        loop.State.Phase = TurnPhase.MapExpand;
        int playerBefore = loop.State.CurrentPlayerIndex;

        loop.SkipPlacement();

        loop.State.Phase.Should().Be(TurnPhase.Action);
        loop.State.CurrentPlayerIndex.Should().NotBe(playerBefore);
    }

    [Fact]
    public void PlaceTile_ThrowsOutsideMapExpand()
    {
        var loop = MakeLoop();
        var tid = loop.State.TileDeck[0];
        var act = () => loop.PlaceTile(tid, 1, 0);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SkipPlacement_CascadesOverConsecutiveSameIdCopies()
    {
        // Simulate a 2-copy corridor: seed deck with [corridorId, corridorId, ...].
        // Skipping the first copy must also pop the second consecutive copy so
        // the following turn does not try to place an orphan corridor piece.
        var loop = MakeLoop();
        loop.State.Phase = TurnPhase.MapExpand;
        loop.State.TileDeck.Clear();
        loop.State.TileDeck.Add("wilderness-path");
        loop.State.TileDeck.Add("wilderness-path");
        loop.State.TileDeck.Add("warehouse");

        loop.SkipPlacement();

        // Both consecutive "wilderness-path" copies should be popped.
        loop.State.TileDeck.Should().Equal("warehouse");
    }
}
