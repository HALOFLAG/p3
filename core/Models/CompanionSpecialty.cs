// CompanionSpecialty — A3 · NPC 同伴 Specialty 規則引擎化。
// 將 Specialty 從顯示字串升級為 (Trigger, Condition?, Effect) 三元組，
// 由 CompanionAbilityHandler 於正確時機自動執行。
using System.Text.Json.Serialization;

namespace CardNarrative.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpecialtyTrigger
{
    BigRoundStart,
    BigRoundEnd,
    OnBattleStart,
    OnBattleEnd,
    OnEventStart,
    OnEventResolved,
    OnEnterTile
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpecialtyEffectDuration
{
    /// <summary>僅本次擲骰生效（下次 PlayCard / ResolveEvent）。</summary>
    NextRoll,
    /// <summary>本事件結算期間生效（至 OnEventResolved 清除）。</summary>
    ThisEvent,
    /// <summary>本戰鬥期間生效（至 OnBattleEnd 清除）。</summary>
    ThisBattle
}

/// <summary>
/// Specialty 條件（選填）。所有欄位皆為 AND 關係；null 代表不限制。
/// </summary>
public sealed record SpecialtyCondition(
    IReadOnlyList<EventType>? EventTypeAny = null,
    bool? TileImportant = null,
    IReadOnlyList<Terrain>? TileTerrainAny = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HealLowestHpEffect),         "healLowestHp")]
[JsonDerivedType(typeof(HealSelfEffect),             "healSelf")]
[JsonDerivedType(typeof(BonusRollEffect),            "bonusRoll")]
[JsonDerivedType(typeof(SpecialtyGrantResourceEffect), "grantResource")]
public abstract record SpecialtyEffect;

public sealed record HealLowestHpEffect(int Amount) : SpecialtyEffect;
public sealed record HealSelfEffect(int Amount) : SpecialtyEffect;
public sealed record BonusRollEffect(int Amount, SpecialtyEffectDuration Duration) : SpecialtyEffect;
/// <summary>Specialty 專用的資源給予效果（與 Effects.cs 的 GrantResourceEffect 不同基底）。</summary>
public sealed record SpecialtyGrantResourceEffect(string Key, int Amount) : SpecialtyEffect;

/// <summary>Specialty 條件化觸發：trigger + 可選 condition + effect。</summary>
public sealed record SpecialtyBinding(
    SpecialtyTrigger Trigger,
    SpecialtyCondition? Condition,
    SpecialtyEffect Effect);
