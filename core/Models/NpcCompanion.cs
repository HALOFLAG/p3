// NpcCompanion — NPC 同伴定義（與玩家共組隊伍）。
// StrategyTable：場景→偏好行動標籤，NpcAi 依此選取行動。
namespace CardNarrative.Core.Models;

public sealed record StrategyEntry(
    string Scene,
    IReadOnlyList<string> PreferredActionTags
);

public sealed record NpcCompanion(
    string Id,
    string Name,
    StatBlock Stats,
    int Hp,
    string Specialty,
    int ActionPoints,
    IReadOnlyList<StrategyEntry> StrategyTable
)
{
    /// <summary>
    /// A3 · 結構化 Specialty 效果：由 CompanionAbilityHandler 於對應 trigger 時機自動執行。
    /// 向後相容：舊模組缺此欄位時為空陣列，引擎略過（`Specialty` 字串仍可顯示）。
    /// </summary>
    public IReadOnlyList<SpecialtyBinding> SpecialtyEffects { get; init; } = Array.Empty<SpecialtyBinding>();
}
