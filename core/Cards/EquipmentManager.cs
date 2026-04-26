using CardNarrative.Core.Models;

namespace CardNarrative.Core.Cards;

/// <summary>
/// 裝備管理（規格書 §3.4.3 3×3 + 背包）。
/// 純資料邏輯，狀態由外部（WorldMap）持有；本類提供操作 + 驗證。
///
/// 8 個主槽（Equipment dict）：
///   Head / Weapon / OffHand / Body / AccessoryA / AccessoryB / Feet / Utility
/// + 背包（Backpack list，上限 3 張，不計算效果）
///
/// 移動規則：
/// - 主槽 ↔ 主槽：必須 IsCompatibleSlot（Equipment.EffectiveAllowedSlots 含目標槽）
///   且若目標槽已有裝備，則該裝備被換到原槽（swap）
/// - 主槽 → 背包：永遠可以（若背包未滿）
/// - 背包 → 主槽：必須 IsCompatibleSlot；目標槽有裝備則 swap 到背包頂端
/// - 背包 ↔ 背包：純位置調整（次序不影響邏輯，但允許重排）
/// </summary>
public static class EquipmentManager
{
    public const int BackpackMax = 3;

    /// <summary>檢查 equipment 是否可放入 targetSlot（依 EffectiveAllowedSlots）。</summary>
    public static bool IsCompatibleSlot(Equipment equipment, EquipmentSlot targetSlot)
    {
        return equipment.EffectiveAllowedSlots.Contains(targetSlot);
    }

    public static MoveEquipmentResult MoveSlotToSlot(
        IDictionary<EquipmentSlot, string?> equipped,
        IReadOnlyDictionary<string, Equipment> catalog,
        EquipmentSlot from,
        EquipmentSlot to)
    {
        if (from == to) return MoveEquipmentResult.NoChange;
        if (!equipped.TryGetValue(from, out var fromId) || fromId is null)
            return MoveEquipmentResult.SourceEmpty;
        if (!catalog.TryGetValue(fromId, out var fromEq))
            return MoveEquipmentResult.UnknownEquipment;
        if (!IsCompatibleSlot(fromEq, to))
            return MoveEquipmentResult.IncompatibleSlot;

        // 目標若有裝備，需可放回 from
        equipped.TryGetValue(to, out var toId);
        if (toId is not null)
        {
            if (!catalog.TryGetValue(toId, out var toEq))
                return MoveEquipmentResult.UnknownEquipment;
            if (!IsCompatibleSlot(toEq, from))
                return MoveEquipmentResult.IncompatibleSlot;
        }

        equipped[to] = fromId;
        equipped[from] = toId; // toId 可能是 null（target 空）
        return MoveEquipmentResult.Ok;
    }

    public static MoveEquipmentResult MoveSlotToBackpack(
        IDictionary<EquipmentSlot, string?> equipped,
        IList<string> backpack,
        EquipmentSlot from)
    {
        if (!equipped.TryGetValue(from, out var fromId) || fromId is null)
            return MoveEquipmentResult.SourceEmpty;
        if (backpack.Count >= BackpackMax)
            return MoveEquipmentResult.BackpackFull;

        backpack.Insert(0, fromId); // 進背包頂端
        equipped[from] = null;
        return MoveEquipmentResult.Ok;
    }

    public static MoveEquipmentResult MoveBackpackToSlot(
        IDictionary<EquipmentSlot, string?> equipped,
        IList<string> backpack,
        IReadOnlyDictionary<string, Equipment> catalog,
        int backpackIndex,
        EquipmentSlot to)
    {
        if (backpackIndex < 0 || backpackIndex >= backpack.Count)
            return MoveEquipmentResult.SourceEmpty;
        var fromId = backpack[backpackIndex];
        if (!catalog.TryGetValue(fromId, out var fromEq))
            return MoveEquipmentResult.UnknownEquipment;
        if (!IsCompatibleSlot(fromEq, to))
            return MoveEquipmentResult.IncompatibleSlot;

        equipped.TryGetValue(to, out var toId);
        // 把背包項取出
        backpack.RemoveAt(backpackIndex);
        // 若目標槽有舊裝備，swap 回背包頂端（規格書 §3.4.3 替換動畫起點）
        if (toId is not null)
        {
            if (backpack.Count >= BackpackMax)
            {
                // 還原狀態
                backpack.Insert(backpackIndex, fromId);
                return MoveEquipmentResult.BackpackFull;
            }
            backpack.Insert(0, toId);
        }
        equipped[to] = fromId;
        return MoveEquipmentResult.Ok;
    }

    /// <summary>把外部新獲得的裝備自動入背包（規格書 §3.4.3）。</summary>
    public static MoveEquipmentResult AddToBackpack(IList<string> backpack, string equipmentId)
    {
        if (backpack.Count >= BackpackMax) return MoveEquipmentResult.BackpackFull;
        backpack.Insert(0, equipmentId); // 新獲得進背包頂端
        return MoveEquipmentResult.Ok;
    }

    /// <summary>
    /// 角色卡 slot ↔ slot 移動：套用「與裝備卡相同」的 swap 規則，但永遠拒絕背包。
    /// 目標若有裝備 → 該裝備搬到角色卡原槽；若該裝備不能放原槽，回 IncompatibleSlot。
    /// </summary>
    public static (MoveEquipmentResult Result, EquipmentSlot NewCharacterSlot) MoveCharacterCardSlotToSlot(
        IDictionary<EquipmentSlot, string?> equipped,
        IReadOnlyDictionary<string, Equipment> catalog,
        EquipmentSlot characterCurrentSlot,
        EquipmentSlot target)
    {
        if (target == characterCurrentSlot) return (MoveEquipmentResult.NoChange, characterCurrentSlot);

        equipped.TryGetValue(target, out var targetId);
        if (targetId is not null)
        {
            if (!catalog.TryGetValue(targetId, out var targetEq))
                return (MoveEquipmentResult.UnknownEquipment, characterCurrentSlot);
            if (!targetEq.EffectiveAllowedSlots.Contains(characterCurrentSlot))
                return (MoveEquipmentResult.IncompatibleSlot, characterCurrentSlot);
            equipped[characterCurrentSlot] = targetId;
            equipped[target] = null;
        }

        return (MoveEquipmentResult.Ok, target);
    }

    /// <summary>
    /// 把裝備從另一槽放進角色卡所在槽：等同 swap — 角色卡移到 sourceSlot，原本在 sourceSlot 的裝備
    /// 進角色卡舊槽。背包來源永遠拒絕（角色卡不可入背包）。
    /// </summary>
    public static (MoveEquipmentResult Result, EquipmentSlot NewCharacterSlot) SwapCharacterWithEquipmentSlot(
        IDictionary<EquipmentSlot, string?> equipped,
        EquipmentSlot characterCurrentSlot,
        EquipmentSlot equipmentSourceSlot)
    {
        if (equipmentSourceSlot == characterCurrentSlot) return (MoveEquipmentResult.NoChange, characterCurrentSlot);
        if (!equipped.TryGetValue(equipmentSourceSlot, out var srcId) || srcId is null)
            return (MoveEquipmentResult.SourceEmpty, characterCurrentSlot);
        equipped[characterCurrentSlot] = srcId;
        equipped[equipmentSourceSlot] = null;
        return (MoveEquipmentResult.Ok, equipmentSourceSlot);
    }
}

public enum MoveEquipmentResult
{
    Ok,
    NoChange,
    SourceEmpty,
    IncompatibleSlot,
    BackpackFull,
    UnknownEquipment,
}
