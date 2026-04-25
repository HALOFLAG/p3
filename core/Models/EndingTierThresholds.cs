// EndingTierThresholds — 結局評分門檻（暫案，對應 V3 dialogs.html 結局視窗 U5）。
// D < 30、C 30–59、B 60–84、A ≥ 85；日後可調。
namespace CardNarrative.Core.Models;

public static class EndingTierThresholds
{
    public const int ATier = 85;
    public const int BTier = 60;
    public const int CTier = 30;

    public static char Tier(int score) =>
        score >= ATier ? 'A'
        : score >= BTier ? 'B'
        : score >= CTier ? 'C'
        : 'D';
}
