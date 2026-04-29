// Prologue — 模組開局設定：勝負條件、難度曲線（早/中/高潮）、
// 起始地塊、重要地塊列表、角色升級門檻、計分公式、開場敘事。
namespace CardNarrative.Core.Models;

public sealed record DifficultyRange(int[] TnRange, int? UntilTurn = null);

public sealed record ClimaxCurve(int BaseTn, int PerFailureTnIncrease, int TnCap);

public sealed record DifficultyCurve(
    DifficultyRange Early,
    DifficultyRange Middle,
    ClimaxCurve Climax
);

public sealed record Prologue(
    string ModuleName,
    int Difficulty,
    string OpeningNarrative,
    IReadOnlyList<WinCondition> WinConditions,
    IReadOnlyList<LoseCondition> LoseConditions,
    DifficultyCurve DifficultyCurve,
    string StartingTileId,
    IReadOnlyList<string> ImportantTileIds,
    int CharacterUpgradeThreshold,
    string ScoreFormula
)
{
    // §13-1 S1 · 開場序幕 overlay 顯示用的人性化標籤。
    // 鍵為 WinCondition.Key / LoseCondition type 的字串表示；找不到時 fallback 到原始欄位。
    // 例：{ "mansion_truth_revealed": "揭開洋房真相" } / { "turnLimit": "30 大回合內完成" }
    public IReadOnlyDictionary<string, string>? WinConditionLabels { get; init; }
    public IReadOnlyDictionary<string, string>? LoseConditionLabels { get; init; }

    /// <summary>
    /// Phase 2 任務 11 Stage 1：起手陣容同伴 ID 清單。對應 npc_companions.json 的 id。
    /// 空清單 / 缺欄位時 runtime fallback 到「無同伴」（CompanionApSubstitution 不發生）。
    /// </summary>
    public IReadOnlyList<string> StartingCompanionIds { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// Phase 3 v1.12 Stage 3 — 模組作者自訂的「3 張選 1」批次抽牌節奏（規格書 §1.5 / §3.1.4）。
    /// 形狀：每組 1-3 張 tile id，外層為批次序列。tile id 重複表多份 copies（與 tiles.json 的 copies 對應）。
    /// 缺欄位 / 空陣列時 runtime fallback 到既有 TileDeck 機械抽 3 張。
    /// 載入時 ModuleLoader 驗證：每組 1-3 張、tile id 必須存在。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> TileBatches { get; init; }
        = System.Array.Empty<IReadOnlyList<string>>();
}
