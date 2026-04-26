// Skill — 角色 / 夥伴的非戰鬥技能（主動 / 被動）。
// LeftPanel 主角區與夥伴區的「技能」列從這裡讀；戰鬥技能仍走 CombatAbility。
using System.Text.Json.Serialization;

namespace CardNarrative.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SkillKind
{
    Active,
    Passive
}

public sealed record Skill(
    string Id,
    string Name,
    SkillKind Kind,
    string Description
);
