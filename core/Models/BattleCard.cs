// BattleCard — 戰鬥卡（敵方定義）。
// 內含 EnemyStats（HP/攻/閃/防/弱點）、EncounterTn（遭遇檢定 TN）、
// EnemyAction 清單（以觸發條件 + 權重篩選）、LootEffects。
// 由 BattleEngine 驅動戰鬥流程，ModuleLoader 從 battles.json 載入。
using System.Text.Json.Serialization;

namespace CardNarrative.Core.Models;

public sealed record EnemyStats(
    int HpMax,
    int AttackPower,
    int Evasion,
    int Defense,
    string? Weakness
);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TargetingRule
{
    LowestHp,
    HighestPower,
    Random
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EnemyActionKind
{
    Prepare,
    Attack,
    UseItem,
    Defend,
    Flee
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AlwaysTrigger), "always")]
[JsonDerivedType(typeof(HpAtMostTrigger), "hpAtMost")]
[JsonDerivedType(typeof(PlayerHpAtMostTrigger), "playerHpAtMost")]
[JsonDerivedType(typeof(RoundAtLeastTrigger), "roundAtLeast")]
[JsonDerivedType(typeof(PreparedLastTurnTrigger), "preparedLastTurn")]
public abstract record EnemyActionTrigger;

public sealed record AlwaysTrigger() : EnemyActionTrigger;
public sealed record HpAtMostTrigger(double Ratio) : EnemyActionTrigger;
public sealed record PlayerHpAtMostTrigger(double Ratio) : EnemyActionTrigger;
public sealed record RoundAtLeastTrigger(int Round) : EnemyActionTrigger;
public sealed record PreparedLastTurnTrigger() : EnemyActionTrigger;

/// <summary>
/// Damage/effect payload for an EnemyAction. Damage is added to the enemy's base AttackPower
/// (for Attack/UseItem kinds). StatusTag is a free-form marker the engine/UI may interpret
/// (e.g. "stunned", "marked"). Prepare actions typically have zero damage and set a status.
/// </summary>
public sealed record EnemyActionPayload(
    int Damage,
    string? StatusTag,
    string? Narrative
);

public sealed record EnemyAction(
    string Id,
    string Name,
    EnemyActionKind Kind,
    TargetingRule Targeting,
    EnemyActionTrigger Trigger,
    int Weight,
    EnemyActionPayload Payload
);

public sealed record BattleCard(
    string Id,
    string Name,
    string Description,
    EnemyStats Stats,
    int EncounterTn,
    IReadOnlyList<EnemyAction> EnemyActions,
    IReadOnlyList<EffectBase> LootEffects
);
