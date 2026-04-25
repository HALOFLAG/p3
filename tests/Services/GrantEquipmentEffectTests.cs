using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class GrantEquipmentEffectTests
{
    [Fact]
    public void Grant_AutoEquips_WhenSlotEmpty()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var handler = new EffectHandler();
        handler.Apply(new GrantEquipmentEffect("hunter-bow"), s, m);
        s.CurrentPlayer.Equipment[EquipmentSlot.Weapon].Should().Be("hunter-bow");
        s.CurrentPlayer.PendingEquipmentGrants.Should().BeEmpty();
    }

    [Fact]
    public void Grant_QueuesAsPending_WhenSlotOccupied()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        s.CurrentPlayer.Equipment[EquipmentSlot.Weapon] = "hunter-bow";
        var handler = new EffectHandler();
        handler.Apply(new GrantEquipmentEffect("hunter-bow"), s, m);
        s.CurrentPlayer.Equipment[EquipmentSlot.Weapon].Should().Be("hunter-bow");
        s.CurrentPlayer.PendingEquipmentGrants.Should().ContainSingle().Which.Should().Be("hunter-bow");
    }

    [Fact]
    public void Grant_QueuesAsPending_WhenSlotOccupiedByCharacterCard()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        s.CurrentPlayer.CharacterCardSlot = EquipmentSlot.Weapon;
        var handler = new EffectHandler();
        handler.Apply(new GrantEquipmentEffect("hunter-bow"), s, m);
        s.CurrentPlayer.Equipment.GetValueOrDefault(EquipmentSlot.Weapon).Should().BeNull();
        s.CurrentPlayer.PendingEquipmentGrants.Should().ContainSingle().Which.Should().Be("hunter-bow");
    }

    [Fact]
    public void Grant_QueuesAsPending_WhenModuleIsNull()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var handler = new EffectHandler();
        handler.Apply(new GrantEquipmentEffect("hunter-bow"), s, null);
        s.CurrentPlayer.PendingEquipmentGrants.Should().ContainSingle();
    }
}
