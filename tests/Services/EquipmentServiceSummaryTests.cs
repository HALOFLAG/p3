using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class EquipmentServiceSummaryTests
{
    [Fact]
    public void ComputeSummary_EmptyEquipment_ReturnsZeros()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;

        var summary = EquipmentService.ComputeSummary(p, m);

        summary.WeaponAttack.Should().Be(0);
        summary.WeaponHitBonus.Should().Be(0);
        summary.ArmorCount.Should().Be(0);
        summary.StatBonuses.Should().Be(new StatBlock(0, 0, 0, 0));
        summary.FlagTokens.Should().BeEmpty();
    }

    [Fact]
    public void ComputeSummary_AggregatesWeaponArmorAndStats()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        p.Equipment[EquipmentSlot.Weapon] = "hunter-bow";       // dmg 3 / hit +2 / power+1 skill+1
        p.Equipment[EquipmentSlot.Body] = "scholar-robes";      // armor, intellect +1
        p.Equipment[EquipmentSlot.Feet] = "leather-boots";      // armor, skill +1

        var summary = EquipmentService.ComputeSummary(p, m);

        summary.WeaponAttack.Should().Be(3);
        summary.WeaponHitBonus.Should().Be(2);
        summary.ArmorCount.Should().Be(2);
        summary.StatBonuses.Power.Should().Be(1);
        summary.StatBonuses.Skill.Should().Be(2);   // bow +1 + boots +1
        summary.StatBonuses.Intellect.Should().Be(1);
        summary.FlagTokens.Should().HaveCount(3);
    }

    [Fact]
    public void ComputeSummary_SkipsSlotOccupiedByCharacterCard()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var p = s.CurrentPlayer;
        p.CharacterCardSlot = EquipmentSlot.Body;
        p.Equipment[EquipmentSlot.Body] = "scholar-robes";  // should be ignored — slot hidden by card
        p.Equipment[EquipmentSlot.Feet] = "leather-boots";  // counted

        var summary = EquipmentService.ComputeSummary(p, m);

        summary.ArmorCount.Should().Be(1);
        summary.StatBonuses.Intellect.Should().Be(0);
        summary.StatBonuses.Skill.Should().Be(1);
        summary.FlagTokens.Should().HaveCount(1);
    }
}
