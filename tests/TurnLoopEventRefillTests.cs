using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests;

public class TurnLoopEventRefillTests
{
    private static (TurnLoop loop, GameState state, Module module) Setup()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        // Warehouse placed adjacent to (0,0) start; Move onto it triggers the event.
        state.TileMap[(1, 0)] = new PlacedTile { TileId = "warehouse", Level = ExplorationLevel.Unfamiliar };
        state.Phase = TurnPhase.Action;
        var loop = new TurnLoop(state, new FakeDiceService(new RollResult(3, 3)), module, new EventScheduler());
        return (loop, state, module);
    }

    [Fact]
    public void BeginEvent_FillsHandToLimit()
    {
        var (loop, state, _) = Setup();
        var player = state.CurrentPlayer;
        // Start from empty hand, some deck
        player.Hand.Clear();
        player.Deck.Clear();
        player.Deck.AddRange(new[] { "persuade", "basic-attack", "careful-search", "integrate-intel", "investigate", "persuade" });
        player.Discard.Clear();

        loop.Move(new Position(1, 0)); // walks onto warehouse → triggers warehouse-investigation

        loop.PendingEvent.Should().NotBeNull();
        player.Hand.Count.Should().Be(5);
    }

    [Fact]
    public void BetweenQueuedEvents_RefillsUpToTwoCards_ThenPresentsNext()
    {
        var (loop, state, module) = Setup();
        var player = state.CurrentPlayer;

        // Trigger first event by walking onto warehouse
        loop.Move(new Position(1, 0));
        loop.PendingEvent.Should().NotBeNull();
        // Queue a second event manually (minimal queue; higher layers may populate later)
        var second = module.Events["warehouse-guard"];
        loop.EnqueueEvent(second);

        // Simulate cards played during first event (hand drops to 3); ensure discard has
        // enough cards for the between-event refill to exercise its +2 cap.
        while (player.Hand.Count > 3)
        {
            player.Discard.Add(player.Hand[0]);
            player.Hand.RemoveAt(0);
        }
        player.Discard.AddRange(new[] { "persuade", "careful-search", "integrate-intel" });

        loop.ResolvePendingEvent();

        loop.PendingEvent!.Id.Should().Be("warehouse-guard");
        // From 3, refill up to 2 → should be 5
        player.Hand.Count.Should().Be(5);
    }

    [Fact]
    public void BetweenQueuedEvents_CapsRefillAtTwo()
    {
        var (loop, state, module) = Setup();
        var player = state.CurrentPlayer;

        loop.Move(new Position(1, 0)); // first event
        loop.EnqueueEvent(module.Events["warehouse-guard"]);

        // Force hand down to 0 so the 2-card cap is observable
        player.Discard.AddRange(player.Hand);
        player.Hand.Clear();

        loop.ResolvePendingEvent();

        player.Hand.Count.Should().Be(2);
    }

    [Fact]
    public void EnqueueEvent_WhenNoPending_BecomesPendingImmediately()
    {
        var (loop, state, module) = Setup();
        var ev = module.Events["warehouse-investigation"];
        loop.EnqueueEvent(ev);

        loop.PendingEvent!.Id.Should().Be("warehouse-investigation");
        state.PendingEventQueue.Should().BeEmpty();
    }
}
