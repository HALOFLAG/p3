using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests;

public class TurnLoopEquipmentBonusTests
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
        return (new TurnLoop(state, new FakeDiceService(new RollResult(3, 3)), module), module);
    }

    [Fact]
    public void StatBonus_AggregatesIntellectEquipment()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        s.CurrentPlayer.Equipment[EquipmentSlot.AccessoryB] = "broken-manifest"; // intellect +1
        EquipmentService.GetStatBonus(s.CurrentPlayer, m, Stat.Intellect).Should().Be(1);
    }

    [Fact]
    public void StatBonus_SkipsCharacterCardSlot()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        p.Equipment[EquipmentSlot.AccessoryB] = "broken-manifest";
        p.CharacterCardSlot = EquipmentSlot.AccessoryB;
        EquipmentService.GetStatBonus(p, m, Stat.Intellect).Should().Be(0);
    }
}
