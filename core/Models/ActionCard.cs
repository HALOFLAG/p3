// ActionCard — 行動卡定義（玩家手牌）。
// 欄位：Id/Name/Type/Cost/Tags/Description + 冷卻（UsesPerTurn/UsesPerEvent）+ Combat（戰鬥效果）。
// 用途：模組 JSON 載入後成為 Module.ActionCards；TurnLoop.PlayCard 依 Type/Cost/冷卻做檢核；
// BattleEngine 於戰鬥中讀取 Combat 欄位決定「傷害倍率」或「判定加成」貢獻。
using System.Text.Json.Serialization;

namespace CardNarrative.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CombatEffectRoute
{
    DamageMultiplier,
    RollBonus
}

/// <summary>
/// Combat-relevant data on an action card. The two routes are mutually exclusive:
/// - DamageMultiplier: if the action hits, final damage is multiplied by DamageMultiplier.
/// - RollBonus: the 2d6 hit check is increased by RollBonus (damage unchanged).
/// Tags can mark the card for reaction use (e.g. "reflect", "reaction-dodge").
/// </summary>
public sealed record CombatEffect(
    CombatEffectRoute Route,
    double DamageMultiplier,
    int RollBonus,
    IReadOnlyList<string> Tags
);

public sealed record ActionCard(
    string Id,
    string Name,
    ActionType Type,
    int Cost,
    IReadOnlyList<string> Tags,
    string Description
)
{
    public int? UsesPerTurn { get; init; }
    public int? UsesPerEvent { get; init; }
    public CombatEffect? Combat { get; init; }
}
