// Equipment — 裝備定義（含 Slot、StatBonuses、Weapon、AllowedSlots）。
// AllowedSlots 讓單件物品可放多個 Slot（Slot 永遠為首選）。
// 用途：由 EquipmentService 計算加值；M9 裝備面板顯示 + 放槽邏輯。
namespace CardNarrative.Core.Models;

public sealed record WeaponStats(int HitBonus, int Damage);

public sealed record Equipment(
    string Id,
    string Name,
    EquipmentSlot Slot,
    StatBlock? StatBonuses,
    string EffectText
)
{
    public ItemCategory Category { get; init; } = ItemCategory.Special;
    // Slots that can hold this item. If empty, defaults to just `Slot`.
    public IReadOnlyList<EquipmentSlot> AllowedSlots { get; init; } = Array.Empty<EquipmentSlot>();
    public WeaponStats? Weapon { get; init; }

    /// <summary>Canonical list of slots this item may occupy (Slot is always first).</summary>
    public IReadOnlyList<EquipmentSlot> EffectiveAllowedSlots
    {
        get
        {
            if (AllowedSlots.Count == 0) return new[] { Slot };
            if (AllowedSlots.Contains(Slot)) return AllowedSlots;
            var list = new List<EquipmentSlot>(AllowedSlots.Count + 1) { Slot };
            list.AddRange(AllowedSlots);
            return list;
        }
    }
}
