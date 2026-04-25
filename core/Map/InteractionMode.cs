namespace CardNarrative.Core.Map;

/// <summary>
/// Phase 1 Task 4 + 5 — 玩家當前的互動模式。
/// 對應規格書 §3.1.2（MapExpand）/§3.1.4（Action）/§5.1.4（移動模式）。
/// </summary>
public enum InteractionMode
{
    /// <summary>預設模式：點玩家格可呼叫觸發器；hover only。</summary>
    Idle,

    /// <summary>
    /// MapExpand：玩家持有一張地塊卡，等待點擊合法 4 方向相鄰未放格放置。
    /// 規格書 §3.1.2。
    /// </summary>
    MapExpand,

    /// <summary>
    /// Move：玩家從觸發器選了「移動」，等待點擊 4 方向相鄰已放格。
    /// 規格書 §3.1.4 / §5.1.4。
    /// </summary>
    Move,
}
