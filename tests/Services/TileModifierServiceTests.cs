using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class TileModifierServiceTests
{
    private static EventCard MakeEvent(string id, EventType type, int tn, Stat stat = Stat.Skill) =>
        new(id, id, type, tn,
            new TileEnterTrigger("wilderness-path"),
            stat,
            new[] { ActionType.Exploration },
            "",
            new EventOutcomes(
                new EventOutcome("s", Array.Empty<EffectBase>()),
                new EventOutcome("p", Array.Empty<EffectBase>()),
                new EventOutcome("f", Array.Empty<EffectBase>())));

    private static GameState PlaceCurrent(Module module, string tileId, ExplorationLevel level = ExplorationLevel.Neutral)
    {
        var state = ModuleFactory.NewState(module);
        state.TileMap[(1, 0)] = new PlacedTile { TileId = tileId, Level = level };
        state.CurrentPlayer.Position = new Position(1, 0);
        return state;
    }

    [Fact]
    public void Town_NegotiationEvent_MinusOneTn()
    {
        var module = ModuleFactory.Load();
        var state = PlaceCurrent(module, "town-square");
        var ev = MakeEvent("talk", EventType.Negotiation, 10, Stat.Social);

        TileModifierService.ComputeEventTn(module, state, ev).Should().Be(9);
    }

    [Fact]
    public void Dungeon_ExplorationEvent_PlusOneTn()
    {
        var module = ModuleFactory.Load();
        var state = PlaceCurrent(module, "warehouse"); // dungeon terrain
        var ev = MakeEvent("search", EventType.Exploration, 10);

        TileModifierService.ComputeEventTn(module, state, ev).Should().Be(11);
    }

    [Fact]
    public void Mastered_ExplorationEvent_MinusOneTn()
    {
        var module = ModuleFactory.Load();
        var state = PlaceCurrent(module, "wilderness-path", ExplorationLevel.Mastered);
        var ev = MakeEvent("search", EventType.Exploration, 10);

        TileModifierService.ComputeEventTn(module, state, ev).Should().Be(9);
    }

    [Fact]
    public void SpecialEvent_AppliesClimaxTnBonus()
    {
        var module = ModuleFactory.Load();
        var state = PlaceCurrent(module, "town-square");
        state.ClimaxTnBonus = 3;
        var ev = MakeEvent("climax", EventType.Special, 14);

        TileModifierService.ComputeEventTn(module, state, ev).Should().Be(17);
    }

    [Fact]
    public void Unknown_ReducesActionCheckBy1()
    {
        var module = ModuleFactory.Load();
        var state = PlaceCurrent(module, "wilderness-path", ExplorationLevel.Unknown);
        var mod = TileModifierService.ComputeActionCheckModifier(module, state, new Position(1, 0), ActionType.Exploration);
        mod.Should().Be(-1);
    }

    [Fact]
    public void Familiar_MatchingTerrain_Adds1()
    {
        var module = ModuleFactory.Load();
        var state = PlaceCurrent(module, "wilderness-path", ExplorationLevel.Familiar);
        var mod = TileModifierService.ComputeActionCheckModifier(module, state, new Position(1, 0), ActionType.Exploration);
        mod.Should().Be(1);
    }

    [Fact]
    public void Mastered_MatchingTerrain_Adds1()
    {
        var module = ModuleFactory.Load();
        var state = PlaceCurrent(module, "wilderness-path", ExplorationLevel.Mastered);
        var mod = TileModifierService.ComputeActionCheckModifier(module, state, new Position(1, 0), ActionType.Exploration);
        mod.Should().Be(1);
    }

    [Fact]
    public void Neutral_NoModifier()
    {
        var module = ModuleFactory.Load();
        var state = PlaceCurrent(module, "wilderness-path", ExplorationLevel.Neutral);
        var mod = TileModifierService.ComputeActionCheckModifier(module, state, new Position(1, 0), ActionType.Exploration);
        mod.Should().Be(0);
    }
}
