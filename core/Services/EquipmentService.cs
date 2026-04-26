// EquipmentService — 裝備相容槽檢查 + 屬性加值彙總。
// CategorySlots 定義每類可放的槽位（武器/防具/飾品/特殊）；
// 聚合所有已裝備（排除 CharacterCardSlot）的 StatBonuses 給行動卡/事件檢定；
// 提供武器命中/傷害取值、腳部加值（用於先攻）。
using CardNarrative.Core.Cards;
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

/// <summary>
/// Slot/category validation and equipment aggregation (StatBonuses, weapon stats).
/// The slot currently occupied by the player's character card is treated as empty
/// (no equipment effect), per design §3-7.
/// </summary>
public static class EquipmentService
{
    // Category → slots that can accept this category of item.
    private static readonly Dictionary<ItemCategory, EquipmentSlot[]> CategorySlots = new()
    {
        [ItemCategory.Weapon] = new[] { EquipmentSlot.Weapon, EquipmentSlot.OffHand },
        [ItemCategory.Armor] = new[]
        {
            EquipmentSlot.OffHand, EquipmentSlot.Head, EquipmentSlot.Body,
            EquipmentSlot.Hand, EquipmentSlot.Feet
        },
        [ItemCategory.Accessory] = new[]
        {
            EquipmentSlot.Hand, EquipmentSlot.Feet,
            EquipmentSlot.AccessoryA, EquipmentSlot.AccessoryB
        },
        [ItemCategory.Special] = new[] { EquipmentSlot.Utility },
    };

    public static IReadOnlyList<EquipmentSlot> GetCompatibleSlots(Equipment eq)
    {
        var declared = eq.EffectiveAllowedSlots;
        if (!CategorySlots.TryGetValue(eq.Category, out var catSlots))
            return declared;
        // Intersect declared with category-allowed; if category matrix has no override, use declared.
        var set = new HashSet<EquipmentSlot>(catSlots);
        var result = new List<EquipmentSlot>();
        foreach (var s in declared) if (set.Contains(s)) result.Add(s);
        return result.Count == 0 ? declared : result;
    }

    public static bool CanEquip(Equipment eq, EquipmentSlot target)
        => GetCompatibleSlots(eq).Contains(target);

    /// <summary>Equip into <paramref name="target"/>. Throws if slot invalid or occupied by character card.</summary>
    public static string? Equip(PlayerState player, Equipment eq, EquipmentSlot target)
    {
        if (target == player.CharacterCardSlot)
            throw new InvalidOperationException($"slot {target} is occupied by the character card");
        if (!CanEquip(eq, target))
            throw new InvalidOperationException($"equipment '{eq.Id}' (category {eq.Category}) cannot occupy slot {target}");
        player.Equipment.TryGetValue(target, out var previous);
        player.Equipment[target] = eq.Id;
        return previous;
    }

    public static string? Unequip(PlayerState player, EquipmentSlot slot)
    {
        if (!player.Equipment.TryGetValue(slot, out var id)) return null;
        player.Equipment[slot] = null;
        return id;
    }

    /// <summary>
    /// Move the character card to <paramref name="target"/>. Same swap semantics as equipment-to-equipment:
    /// if target slot holds equipment, that equipment migrates to the character card's previous slot
    /// (must be a compatible slot for it). Returns <see cref="MoveEquipmentResult.IncompatibleSlot"/>
    /// if the displaced equipment cannot fit the source slot. Backpack is never a valid target.
    /// </summary>
    public static MoveEquipmentResult MoveCharacterCard(PlayerState player, Module module, EquipmentSlot target)
    {
        if (target == player.CharacterCardSlot) return MoveEquipmentResult.NoChange;

        var source = player.CharacterCardSlot;
        player.Equipment.TryGetValue(target, out var targetId);

        if (targetId is not null)
        {
            if (!module.Equipment.TryGetValue(targetId, out var targetEq))
                return MoveEquipmentResult.UnknownEquipment;
            if (!targetEq.EffectiveAllowedSlots.Contains(source))
                return MoveEquipmentResult.IncompatibleSlot;
            player.Equipment[source] = targetId;
            player.Equipment[target] = null;
        }

        player.CharacterCardSlot = target;
        return MoveEquipmentResult.Ok;
    }

    /// <summary>
    /// Drop equipment onto the slot currently holding the character card. The character card swaps to
    /// <paramref name="equipmentSourceSlot"/>; only valid when the source is a primary slot (backpack
    /// rejected — the character card cannot enter the backpack).
    /// </summary>
    public static MoveEquipmentResult SwapCharacterWithEquipmentSlot(
        PlayerState player,
        EquipmentSlot equipmentSourceSlot)
    {
        if (equipmentSourceSlot == player.CharacterCardSlot) return MoveEquipmentResult.NoChange;
        if (!player.Equipment.TryGetValue(equipmentSourceSlot, out var srcId) || srcId is null)
            return MoveEquipmentResult.SourceEmpty;
        // Caller pre-validates srcEq.EffectiveAllowedSlots.Contains(player.CharacterCardSlot);
        // we move the equipment into the character's old slot regardless to keep this routine simple.
        player.Equipment[player.CharacterCardSlot] = srcId;
        player.Equipment[equipmentSourceSlot] = null;
        player.CharacterCardSlot = equipmentSourceSlot;
        return MoveEquipmentResult.Ok;
    }

    /// <summary>UI helper — returns character base stats plus equipment bonuses (skipping char card's slot).</summary>
    public static StatBlock GetTotalStats(PlayerState player, Module module)
    {
        if (!module.Characters.TryGetValue(player.CharacterId, out var character))
            return AggregateStatBonuses(player, module);
        var bonus = AggregateStatBonuses(player, module);
        return new StatBlock(
            character.Stats.Power + bonus.Power,
            character.Stats.Social + bonus.Social,
            character.Stats.Skill + bonus.Skill,
            character.Stats.Intellect + bonus.Intellect);
    }

    /// <summary>Sum StatBonuses across all equipped items (skipping the slot occupied by the character card).</summary>
    public static StatBlock AggregateStatBonuses(PlayerState player, Module module)
    {
        int power = 0, social = 0, skill = 0, intellect = 0;
        foreach (var (slot, id) in player.Equipment)
        {
            if (slot == player.CharacterCardSlot) continue;
            if (id is null) continue;
            if (!module.Equipment.TryGetValue(id, out var eq)) continue;
            if (eq.StatBonuses is null) continue;
            power += eq.StatBonuses.Power;
            social += eq.StatBonuses.Social;
            skill += eq.StatBonuses.Skill;
            intellect += eq.StatBonuses.Intellect;
        }
        return new StatBlock(power, social, skill, intellect);
    }

    public static int GetStatBonus(PlayerState player, Module module, Stat stat)
    {
        var agg = AggregateStatBonuses(player, module);
        return stat switch
        {
            Stat.Power => agg.Power,
            Stat.Social => agg.Social,
            Stat.Skill => agg.Skill,
            Stat.Intellect => agg.Intellect,
            _ => 0
        };
    }

    /// <summary>Weapon stats for the player's primary Weapon slot (null if empty or not a weapon, or slot occupied by character card).</summary>
    public static WeaponStats? GetWeaponStats(PlayerState player, Module module)
    {
        if (player.CharacterCardSlot == EquipmentSlot.Weapon) return null;
        if (!player.Equipment.TryGetValue(EquipmentSlot.Weapon, out var id) || id is null) return null;
        if (!module.Equipment.TryGetValue(id, out var eq)) return null;
        return eq.Weapon;
    }

    /// <summary>Bonus applied to initiative (2d6 + Skill + Feet equipment bonus). Returns Feet item's StatBonuses.Skill or 0.</summary>
    public static int GetInitiativeBonus(PlayerState player, Module module)
    {
        if (player.CharacterCardSlot == EquipmentSlot.Feet) return 0;
        if (!player.Equipment.TryGetValue(EquipmentSlot.Feet, out var id) || id is null) return 0;
        if (!module.Equipment.TryGetValue(id, out var eq)) return 0;
        return eq.StatBonuses?.Skill ?? 0;
    }

    /// <summary>
    /// Roll-up of equipment state consumed by the UI's equipment summary row (M-UI-3).
    /// WeaponAttack / WeaponHit read from the Weapon slot; ArmorCount tallies any equipped item
    /// with Category=Armor; StatBonuses is the same aggregate as <see cref="AggregateStatBonuses"/>;
    /// FlagTokens collects non-stat descriptive tokens from each item's EffectText so the summary
    /// row can show free-form旗標 like「商隊名冊」without inventing new equipment fields.
    /// </summary>
    public static EquipmentSummary ComputeSummary(PlayerState player, Module module)
    {
        int weaponAttack = 0, weaponHit = 0, armorCount = 0;
        var flags = new List<string>();
        foreach (var (slot, id) in player.Equipment)
        {
            if (slot == player.CharacterCardSlot) continue;
            if (id is null) continue;
            if (!module.Equipment.TryGetValue(id, out var eq)) continue;
            if (slot == EquipmentSlot.Weapon && eq.Weapon is { } w)
            {
                weaponAttack += w.Damage;
                weaponHit += w.HitBonus;
            }
            if (eq.Category == ItemCategory.Armor) armorCount++;
            if (!string.IsNullOrWhiteSpace(eq.EffectText))
                flags.Add(eq.EffectText);
        }
        var stats = AggregateStatBonuses(player, module);
        return new EquipmentSummary(weaponAttack, weaponHit, armorCount, stats, flags);
    }
}

/// <summary>UI-facing aggregate of a player's current equipment; see EquipmentService.ComputeSummary.</summary>
public sealed record EquipmentSummary(
    int WeaponAttack,
    int WeaponHitBonus,
    int ArmorCount,
    StatBlock StatBonuses,
    IReadOnlyList<string> FlagTokens
);
