// NpcCompanion — NPC 同伴定義（與玩家共組隊伍）。
// StrategyTable：場景→偏好行動標籤，NpcAi 依此選取行動。
// Description / Personality / SceneResponses / Skills 為「夥伴卡」UI 用：
//   · Description：背景描述（LeftPanel 夥伴區）
//   · Personality：性格描述（事件提示 fallback 文案）
//   · SceneResponses：依事件 scene 顯示的對話建議文字（EventResolutionDialog 提示）
//   · Skills：非戰鬥主動 / 被動技能（戰鬥技能仍走 CombatAbility 於 SpecialtyEffects 之外）
namespace CardNarrative.Core.Models;

public sealed record StrategyEntry(
    string Scene,
    IReadOnlyList<string> PreferredActionTags
);

public sealed record SceneResponse(
    string Scene,
    string Text
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

    public string Description { get; init; } = string.Empty;
    public string Personality { get; init; } = string.Empty;
    public IReadOnlyList<SceneResponse> SceneResponses { get; init; } = Array.Empty<SceneResponse>();
    public IReadOnlyList<Skill> Skills { get; init; } = Array.Empty<Skill>();
}
