namespace CardNarrative.Core.Cards;

/// <summary>
/// 同伴在當前回合的可變狀態（規格書 §3.7）。
/// CompanionId 對應 npc_companion.json 的 id；RemainingAp 每回合 Draw 階段重置至上限。
/// </summary>
public sealed class CompanionAiState
{
    public string CompanionId { get; }
    public string DisplayName { get; }
    public int RemainingAp { get; set; }
    public int MaxAp { get; }

    public CompanionAiState(string companionId, string displayName, int maxAp)
    {
        CompanionId = companionId;
        DisplayName = displayName;
        MaxAp = maxAp;
        RemainingAp = maxAp;
    }

    /// <summary>每回合 Draw 階段呼叫，重置至上限。</summary>
    public void ResetForNewTurn() => RemainingAp = MaxAp;
}
