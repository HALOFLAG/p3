using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class EquipmentServiceTests
{
    [Fact]
    public void Equip_RejectsCharacterCardSlot()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        p.CharacterCardSlot = EquipmentSlot.Body;
        var body = m.Equipment["scholar-robes"];
        Action act = () => EquipmentService.Equip(p, body, EquipmentSlot.Body);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Equip_RejectsIncompatibleSlot()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        var weapon = m.Equipment["hunter-bow"];
        Action act = () => EquipmentService.Equip(p, weapon, EquipmentSlot.AccessoryA);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Equip_AllowsSlotInAllowedSlots()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        var weapon = m.Equipment["hunter-bow"];
        EquipmentService.Equip(p, weapon, EquipmentSlot.OffHand);
        p.Equipment[EquipmentSlot.OffHand].Should().Be("hunter-bow");
    }

    [Fact]
    public void AggregateStatBonuses_SumsAllEquipped()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        p.Equipment[EquipmentSlot.Body] = "scholar-robes";          // intellect +1
        p.Equipment[EquipmentSlot.AccessoryB] = "broken-manifest";  // intellect +1
        var agg = EquipmentService.AggregateStatBonuses(p, m);
        agg.Intellect.Should().Be(2);
        agg.Skill.Should().Be(0);
    }

    [Fact]
    public void AggregateStatBonuses_SkipsSlotOccupiedByCharacterCard()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        p.Equipment[EquipmentSlot.Body] = "scholar-robes";
        p.CharacterCardSlot = EquipmentSlot.Body;
        var agg = EquipmentService.AggregateStatBonuses(p, m);
        agg.Intellect.Should().Be(0);
    }

    [Fact]
    public void GetWeaponStats_ReturnsNull_WhenSlotEmpty()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        EquipmentService.GetWeaponStats(s.CurrentPlayer, m).Should().BeNull();
    }

    [Fact]
    public void GetWeaponStats_ReturnsWeaponStats_WhenEquipped()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        s.CurrentPlayer.Equipment[EquipmentSlot.Weapon] = "hunter-bow";
        var w = EquipmentService.GetWeaponStats(s.CurrentPlayer, m);
        w.Should().NotBeNull();
        w!.HitBonus.Should().Be(2);
        w.Damage.Should().Be(3);
    }

    [Fact]
    public void GetInitiativeBonus_UsesFeetSkillBonus()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        s.CurrentPlayer.Equipment[EquipmentSlot.Feet] = "leather-boots";
        EquipmentService.GetInitiativeBonus(s.CurrentPlayer, m).Should().Be(1);
    }

    [Fact]
    public void GetInitiativeBonus_ZeroWhenCharCardOccupiesFeet()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        s.CurrentPlayer.Equipment[EquipmentSlot.Feet] = "leather-boots";
        s.CurrentPlayer.CharacterCardSlot = EquipmentSlot.Feet;
        EquipmentService.GetInitiativeBonus(s.CurrentPlayer, m).Should().Be(0);
    }

    [Fact]
    public void MoveCharacterCard_DisplacesItemInTarget()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        p.Equipment[EquipmentSlot.Head] = "scholar-robes"; // id is opaque here; test move semantics
        var displaced = EquipmentService.MoveCharacterCard(p, EquipmentSlot.Head);
        displaced.Should().Be("scholar-robes");
        p.CharacterCardSlot.Should().Be(EquipmentSlot.Head);
        p.Equipment[EquipmentSlot.Head].Should().BeNull();
    }
}
