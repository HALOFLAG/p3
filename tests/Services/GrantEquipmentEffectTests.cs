using CardNarrative.Core.Cards;
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
        s.CurrentPlayer.Backpack.Should().BeEmpty();
        s.CurrentPlayer.PendingEquipmentGrants.Should().BeEmpty();
    }

    [Fact]
    public void Grant_GoesToBackpack_WhenSlotOccupied()
    {
        // PR-A：規格書 §3.4.3 「獲得即入背包」— 主槽被占時優先入背包，不再進 PendingEquipmentGrants。
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        s.CurrentPlayer.Equipment[EquipmentSlot.Weapon] = "hunter-bow";
        var handler = new EffectHandler();
        handler.Apply(new GrantEquipmentEffect("hunter-bow"), s, m);
        s.CurrentPlayer.Equipment[EquipmentSlot.Weapon].Should().Be("hunter-bow");
        s.CurrentPlayer.Backpack.Should().ContainSingle().Which.Should().Be("hunter-bow");
        s.CurrentPlayer.PendingEquipmentGrants.Should().BeEmpty();
    }

    [Fact]
    public void Grant_GoesToBackpack_WhenSlotOccupiedByCharacterCard()
    {
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        s.CurrentPlayer.CharacterCardSlot = EquipmentSlot.Weapon;
        var handler = new EffectHandler();
        handler.Apply(new GrantEquipmentEffect("hunter-bow"), s, m);
        s.CurrentPlayer.Equipment.GetValueOrDefault(EquipmentSlot.Weapon).Should().BeNull();
        s.CurrentPlayer.Backpack.Should().ContainSingle().Which.Should().Be("hunter-bow");
        s.CurrentPlayer.PendingEquipmentGrants.Should().BeEmpty();
    }

    [Fact]
    public void Grant_GoesToBackpack_WhenModuleIsNull()
    {
        // module 為 null（未知裝備來源）— 略過 auto-equip，直接入背包。
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        var handler = new EffectHandler();
        handler.Apply(new GrantEquipmentEffect("hunter-bow"), s, null);
        s.CurrentPlayer.Backpack.Should().ContainSingle().Which.Should().Be("hunter-bow");
        s.CurrentPlayer.PendingEquipmentGrants.Should().BeEmpty();
    }

    [Fact]
    public void Grant_QueuesAsPending_WhenBackpackFull()
    {
        // PR-A 新覆蓋：當 auto-equip 無法（slot 被占）且背包也滿，才走 PendingEquipmentGrants。
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        s.CurrentPlayer.Equipment[EquipmentSlot.Weapon] = "hunter-bow";
        // 灌滿背包到 BackpackMax (= 3)
        for (int i = 0; i < EquipmentManager.BackpackMax; i++)
            s.CurrentPlayer.Backpack.Add($"filler-{i}");

        var handler = new EffectHandler();
        handler.Apply(new GrantEquipmentEffect("hunter-bow"), s, m);

        s.CurrentPlayer.Backpack.Count.Should().Be(EquipmentManager.BackpackMax);
        s.CurrentPlayer.PendingEquipmentGrants.Should().ContainSingle().Which.Should().Be("hunter-bow");
    }

    [Fact]
    public void Grant_BackpackInserts_AtTopWhenSpaceAvailable()
    {
        // PR-A：與 EquipmentManager.AddToBackpack 行為一致（新獲得進背包頂端 index 0）。
        var m = ModuleFactory.Load();
        var s = ModuleFactory.NewState(m);
        s.CurrentPlayer.Equipment[EquipmentSlot.Weapon] = "hunter-bow"; // 阻擋 auto-equip
        s.CurrentPlayer.Backpack.Add("existing-1");

        var handler = new EffectHandler();
        handler.Apply(new GrantEquipmentEffect("hunter-bow"), s, m);

        s.CurrentPlayer.Backpack[0].Should().Be("hunter-bow");
        s.CurrentPlayer.Backpack[1].Should().Be("existing-1");
        s.CurrentPlayer.PendingEquipmentGrants.Should().BeEmpty();
    }
}
