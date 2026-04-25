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
}
