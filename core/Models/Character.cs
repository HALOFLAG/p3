// Character — 可選角色定義（玩家在 Setup 挑選後成為 PlayerState）。
// StatBlock 共四維：Power/Social/Skill/Intellect，對應事件/行動檢定。
// StartingDeck 為開局牌庫（id 列表），Specialty 目前為顯示文字。
// CombatAbilities 為每場戰鬥冷卻的角色專屬行動（空列表代表該角色無特殊戰鬥能力）。
namespace CardNarrative.Core.Models;

public sealed record StatBlock(int Power, int Social, int Skill, int Intellect);

/// <summary>
/// A once-per-battle ability attached to a Character. Affects the 2d6 hit check via RollBonus
/// and the final damage via DamageMultiplier (both applied if set). Description is UI-facing.
/// </summary>
public sealed record CombatAbility(
    string Id,
    string Name,
    string Description,
    int RollBonus,
    double DamageMultiplier
);

public sealed record Character(
    string Id,
    string Name,
    StatBlock Stats,
    int HpMax,
    string Specialty,
    IReadOnlyList<string> StartingDeck
)
{
    public IReadOnlyList<CombatAbility> CombatAbilities { get; init; } = Array.Empty<CombatAbility>();
}
