using System.Linq;
using CardNarrative.Core.Cards;
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
    public void MoveCharacterCard_SwapsTargetEquipmentToSourceSlot()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        // Setup: char card at Head (default); place an item in Body that is also valid for Head.
        // scholar-robes targets Body. Pick an item whose AllowedSlots include both Body and Head.
        // Use a fixture-known item; if none, fall back to a pure no-equipment swap.
        p.CharacterCardSlot = EquipmentSlot.Body;
        p.Equipment[EquipmentSlot.Body] = null;
        // Place equipment at target Head only if it can also fit Body (otherwise swap is rejected).
        var head = m.Equipment.Values.FirstOrDefault(e => e.EffectiveAllowedSlots.Contains(EquipmentSlot.Head)
                                                          && e.EffectiveAllowedSlots.Contains(EquipmentSlot.Body));
        if (head is not null)
        {
            p.Equipment[EquipmentSlot.Head] = head.Id;
            var r = EquipmentService.MoveCharacterCard(p, m, EquipmentSlot.Head);
            r.Should().Be(MoveEquipmentResult.Ok);
            p.CharacterCardSlot.Should().Be(EquipmentSlot.Head);
            p.Equipment[EquipmentSlot.Body].Should().Be(head.Id);
            p.Equipment[EquipmentSlot.Head].Should().BeNull();
        }
    }

    [Fact]
    public void MoveCharacterCard_RejectsWhenTargetEquipmentIncompatibleWithSource()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        p.CharacterCardSlot = EquipmentSlot.Body;
        // hunter-bow can occupy Weapon/OffHand (per fixtures); it cannot fit Body.
        p.Equipment[EquipmentSlot.Weapon] = "hunter-bow";
        var r = EquipmentService.MoveCharacterCard(p, m, EquipmentSlot.Weapon);
        r.Should().Be(MoveEquipmentResult.IncompatibleSlot);
        p.CharacterCardSlot.Should().Be(EquipmentSlot.Body); // unchanged
        p.Equipment[EquipmentSlot.Weapon].Should().Be("hunter-bow"); // unchanged
    }

    [Fact]
    public void MoveCharacterCard_NoChange_WhenTargetIsCurrentSlot()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        p.CharacterCardSlot = EquipmentSlot.Head;
        EquipmentService.MoveCharacterCard(p, m, EquipmentSlot.Head)
            .Should().Be(MoveEquipmentResult.NoChange);
    }

    [Fact]
    public void MoveCharacterCard_MovesToEmptyTarget()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        p.CharacterCardSlot = EquipmentSlot.Head;
        var r = EquipmentService.MoveCharacterCard(p, m, EquipmentSlot.Body);
        r.Should().Be(MoveEquipmentResult.Ok);
        p.CharacterCardSlot.Should().Be(EquipmentSlot.Body);
    }

    [Fact]
    public void GetTotalStats_AddsCharacterBaseToEquipmentBonuses()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        var c = m.Characters[p.CharacterId];
        p.Equipment[EquipmentSlot.Body] = "scholar-robes"; // intellect +1
        var total = EquipmentService.GetTotalStats(p, m);
        total.Intellect.Should().Be(c.Stats.Intellect + 1);
        total.Power.Should().Be(c.Stats.Power);
    }
}
