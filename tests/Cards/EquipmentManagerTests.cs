using CardNarrative.Core.Cards;
using CardNarrative.Core.Models;
using FluentAssertions;

namespace CardNarrative.Tests.Cards;

public class EquipmentManagerTests
{
    private static Equipment MakeEquipment(string id, EquipmentSlot primary, params EquipmentSlot[] extra)
    {
        return new Equipment(id, id, primary, null, "")
        {
            AllowedSlots = extra.Length == 0 ? Array.Empty<EquipmentSlot>() : extra,
        };
    }

    private sealed record CatalogItem(string Id, EquipmentSlot Slot, EquipmentSlot[]? Extra = null);

    private static (Dictionary<EquipmentSlot, string?> equipped, List<string> backpack, Dictionary<string, Equipment> catalog)
        Setup(params CatalogItem[] items)
    {
        var equipped = new Dictionary<EquipmentSlot, string?>();
        foreach (EquipmentSlot s in Enum.GetValues<EquipmentSlot>()) equipped[s] = null;
        var backpack = new List<string>();
        var catalog = new Dictionary<string, Equipment>();
        foreach (var item in items)
        {
            catalog[item.Id] = MakeEquipment(item.Id, item.Slot, item.Extra ?? Array.Empty<EquipmentSlot>());
        }
        return (equipped, backpack, catalog);
    }

    [Fact]
    public void IsCompatibleSlot_PrimarySlot_True()
    {
        var eq = MakeEquipment("sword", EquipmentSlot.Weapon);
        EquipmentManager.IsCompatibleSlot(eq, EquipmentSlot.Weapon).Should().BeTrue();
    }

    [Fact]
    public void IsCompatibleSlot_AllowedExtraSlot_True()
    {
        var eq = MakeEquipment("ring", EquipmentSlot.AccessoryA, EquipmentSlot.AccessoryB);
        EquipmentManager.IsCompatibleSlot(eq, EquipmentSlot.AccessoryB).Should().BeTrue();
    }

    [Fact]
    public void IsCompatibleSlot_DisallowedSlot_False()
    {
        var eq = MakeEquipment("sword", EquipmentSlot.Weapon);
        EquipmentManager.IsCompatibleSlot(eq, EquipmentSlot.Head).Should().BeFalse();
    }

    [Fact]
    public void MoveSlotToSlot_TargetEmpty_MovesAndClearsSource()
    {
        var (eq, _, cat) = Setup(new CatalogItem("sword", EquipmentSlot.Weapon, new[] { EquipmentSlot.OffHand }));
        eq[EquipmentSlot.Weapon] = "sword";

        var r = EquipmentManager.MoveSlotToSlot(eq, cat, EquipmentSlot.Weapon, EquipmentSlot.OffHand);

        r.Should().Be(MoveEquipmentResult.Ok);
        eq[EquipmentSlot.Weapon].Should().BeNull();
        eq[EquipmentSlot.OffHand].Should().Be("sword");
    }

    [Fact]
    public void MoveSlotToSlot_TargetOccupiedAndCompatible_Swaps()
    {
        var (eq, _, cat) = Setup(
            new CatalogItem("ringA", EquipmentSlot.AccessoryA, new[] { EquipmentSlot.AccessoryB }),
            new CatalogItem("ringB", EquipmentSlot.AccessoryB, new[] { EquipmentSlot.AccessoryA }));
        eq[EquipmentSlot.AccessoryA] = "ringA";
        eq[EquipmentSlot.AccessoryB] = "ringB";

        var r = EquipmentManager.MoveSlotToSlot(eq, cat, EquipmentSlot.AccessoryA, EquipmentSlot.AccessoryB);

        r.Should().Be(MoveEquipmentResult.Ok);
        eq[EquipmentSlot.AccessoryA].Should().Be("ringB");
        eq[EquipmentSlot.AccessoryB].Should().Be("ringA");
    }

    [Fact]
    public void MoveSlotToSlot_IncompatibleTarget_Rejects()
    {
        var (eq, _, cat) = Setup(new CatalogItem("sword", EquipmentSlot.Weapon));
        eq[EquipmentSlot.Weapon] = "sword";

        var r = EquipmentManager.MoveSlotToSlot(eq, cat, EquipmentSlot.Weapon, EquipmentSlot.Head);

        r.Should().Be(MoveEquipmentResult.IncompatibleSlot);
        eq[EquipmentSlot.Weapon].Should().Be("sword");
    }

    [Fact]
    public void MoveSlotToSlot_SourceEmpty_Rejects()
    {
        var (eq, _, cat) = Setup();

        var r = EquipmentManager.MoveSlotToSlot(eq, cat, EquipmentSlot.Weapon, EquipmentSlot.OffHand);

        r.Should().Be(MoveEquipmentResult.SourceEmpty);
    }

    [Fact]
    public void MoveSlotToBackpack_HasItem_AddsToBackpack()
    {
        var (eq, bp, _) = Setup(new CatalogItem("sword", EquipmentSlot.Weapon));
        eq[EquipmentSlot.Weapon] = "sword";

        var r = EquipmentManager.MoveSlotToBackpack(eq, bp, EquipmentSlot.Weapon);

        r.Should().Be(MoveEquipmentResult.Ok);
        eq[EquipmentSlot.Weapon].Should().BeNull();
        bp.Should().ContainSingle().Which.Should().Be("sword");
    }

    [Fact]
    public void MoveSlotToBackpack_BackpackFull_Rejects()
    {
        var (eq, bp, _) = Setup(new CatalogItem("sword", EquipmentSlot.Weapon));
        eq[EquipmentSlot.Weapon] = "sword";
        bp.Add("a"); bp.Add("b"); bp.Add("c");

        var r = EquipmentManager.MoveSlotToBackpack(eq, bp, EquipmentSlot.Weapon);

        r.Should().Be(MoveEquipmentResult.BackpackFull);
        eq[EquipmentSlot.Weapon].Should().Be("sword");
        bp.Should().HaveCount(3);
    }

    [Fact]
    public void MoveBackpackToSlot_TargetEmpty_EquipsAndRemovesFromBackpack()
    {
        var (eq, bp, cat) = Setup(new CatalogItem("sword", EquipmentSlot.Weapon));
        bp.Add("sword");

        var r = EquipmentManager.MoveBackpackToSlot(eq, bp, cat, 0, EquipmentSlot.Weapon);

        r.Should().Be(MoveEquipmentResult.Ok);
        eq[EquipmentSlot.Weapon].Should().Be("sword");
        bp.Should().BeEmpty();
    }

    [Fact]
    public void MoveBackpackToSlot_TargetOccupied_SwapsToBackpack()
    {
        var (eq, bp, cat) = Setup(
            new CatalogItem("sword", EquipmentSlot.Weapon),
            new CatalogItem("axe", EquipmentSlot.Weapon));
        eq[EquipmentSlot.Weapon] = "sword";
        bp.Add("axe");

        var r = EquipmentManager.MoveBackpackToSlot(eq, bp, cat, 0, EquipmentSlot.Weapon);

        r.Should().Be(MoveEquipmentResult.Ok);
        eq[EquipmentSlot.Weapon].Should().Be("axe");
        bp.Should().ContainSingle().Which.Should().Be("sword");
    }

    [Fact]
    public void MoveBackpackToSlot_IncompatibleSlot_Rejects()
    {
        var (eq, bp, cat) = Setup(new CatalogItem("sword", EquipmentSlot.Weapon));
        bp.Add("sword");

        var r = EquipmentManager.MoveBackpackToSlot(eq, bp, cat, 0, EquipmentSlot.Head);

        r.Should().Be(MoveEquipmentResult.IncompatibleSlot);
        bp.Should().ContainSingle();
        eq[EquipmentSlot.Head].Should().BeNull();
    }

    [Fact]
    public void AddToBackpack_BelowMax_Adds()
    {
        var bp = new List<string>();

        EquipmentManager.AddToBackpack(bp, "sword").Should().Be(MoveEquipmentResult.Ok);
        bp.Should().ContainSingle().Which.Should().Be("sword");
    }

    [Fact]
    public void AddToBackpack_AtMax_Rejects()
    {
        var bp = new List<string> { "a", "b", "c" };

        EquipmentManager.AddToBackpack(bp, "d").Should().Be(MoveEquipmentResult.BackpackFull);
        bp.Should().HaveCount(3);
    }
}
