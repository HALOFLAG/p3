using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class TurnLoopEventTests
{
    [Fact]
    public void Move_OntoWarehouseTile_SetsPendingEvent()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(1, 0)] = new PlacedTile { TileId = "warehouse" };
        state.Phase = TurnPhase.Action;
        var loop = new TurnLoop(state, new FakeDiceService(), module, new EventScheduler());

        loop.Move(new Position(1, 0));

        loop.PendingEvent.Should().NotBeNull();
        loop.PendingEvent!.Id.Should().Be("warehouse-investigation");
        loop.State.Phase.Should().Be(TurnPhase.Action); // onEnter events keep us in Action
    }

    [Fact]
    public void Advance_WhilePending_Throws()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(1, 0)] = new PlacedTile { TileId = "warehouse" };
        state.Phase = TurnPhase.Action;
        var loop = new TurnLoop(state, new FakeDiceService(), module, new EventScheduler());
        loop.Move(new Position(1, 0));

        var act = () => loop.Advance();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolvePendingEvent_ClearsAndReturnsToAction()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(1, 0)] = new PlacedTile { TileId = "warehouse" };
        state.Phase = TurnPhase.Action;
        var loop = new TurnLoop(state, new FakeDiceService(), module, new EventScheduler());
        loop.Move(new Position(1, 0));

        loop.ResolvePendingEvent();

        loop.PendingEvent.Should().BeNull();
        loop.State.Phase.Should().Be(TurnPhase.Action);
    }
}
