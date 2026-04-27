// MessageBubbleService — 訊息氣泡管理（規格書 §1.9）。
// 仿 TurnLogService 結構（環形緩衝 + 事件），但語意不同：
//   - TurnLog：50 筆環形，跨回合保留，所有 effect 套用日誌
//   - MessageBubble：當回合容量 20，TurnEnd 清空，4 種來源（OrbitSlot/CompanionCard/SystemHint/PendingTask）
// 由 EventResolver / NpcAi / TurnLoop / MainBootstrap.OnEventResolved push；
// Phase 2 唯一接好的 push 入口為 MainBootstrap（OrbitSlot 來源）；其他來源待 Task 14 接 NpcAi/TurnLoop。
using CardNarrative.Core.Models;

namespace CardNarrative.Core.Services;

/// <summary>
/// 規格書 §1.9 顯示時機：MapExpand / Action / EventCheck 顯示；TurnEnd 隱藏。
/// 與 GameState.TurnPhase 同 4 值對齊（除去 Draw — 規格書 §1.9 不在 Draw 階段顯示）。
/// Phase 2 runtime 沒有真正的 TurnPhase，由 caller 用 Func 探針餵 Action（一律顯示）；
/// TODO Task 14：TurnLoop 接通後改用真實 GameState.Phase 探針。
/// </summary>
public enum MessageBubbleVisibilityPhase
{
    MapExpand,
    Action,
    EventCheck,
    TurnEnd,
}

public enum NavigationKind
{
    Orbit,
    Companion,
}

/// <summary>
/// 訊息氣泡跳轉目標。Kind=Orbit 時 SlotIndex 有值（ORBIT 第 N 槽）；
/// Kind=Companion 時 TargetId 有值（companion id，UI 反查 LeftPanel 同伴卡）。
/// </summary>
public sealed record NavigationTarget(NavigationKind Kind, string TargetId, int? SlotIndex);

public sealed class MessageBubbleService
{
    public const int MaxPerTurn = 20;

    private readonly LinkedList<MessageBubble> _bubbles = new();
    private int _nextId;

    public IReadOnlyCollection<MessageBubble> Bubbles => _bubbles;

    public event EventHandler<MessageBubble>? MessagePushed;
    public event EventHandler? MessageCleared;

    /// <summary>
    /// 推一筆新訊息。Id 由 service 自動分配（caller 傳 -1 或任意值都會被覆蓋）。
    /// 容量到 MaxPerTurn 時丟最舊（環形）；timestamp 由 caller 給（測試可注入固定值）。
    /// </summary>
    public MessageBubble Push(string text, MessageBubbleSource source, string sourceId, DateTime timestamp, bool isImportant)
    {
        var bubble = new MessageBubble(
            Id: _nextId++,
            Text: text,
            Source: source,
            SourceId: sourceId,
            Timestamp: timestamp,
            IsImportant: isImportant);
        _bubbles.AddLast(bubble);
        while (_bubbles.Count > MaxPerTurn)
            _bubbles.RemoveFirst();
        MessagePushed?.Invoke(this, bubble);
        return bubble;
    }

    /// <summary>
    /// 階段過濾：MapExpand / Action / EventCheck → 全部訊息（最新在上）；TurnEnd → 空 list。
    /// </summary>
    public IReadOnlyList<MessageBubble> GetVisible(MessageBubbleVisibilityPhase phase)
    {
        if (phase == MessageBubbleVisibilityPhase.TurnEnd)
            return Array.Empty<MessageBubble>();
        // 最新在上：反序輸出
        var list = new List<MessageBubble>(_bubbles.Count);
        for (var node = _bubbles.Last; node is not null; node = node.Previous)
            list.Add(node.Value);
        return list;
    }

    /// <summary>TurnEnd 時呼叫，清空所有當回合訊息。Id 計數不重置（避免存檔讀檔對不上）。</summary>
    public void OnTurnEnd()
    {
        if (_bubbles.Count == 0) return;
        _bubbles.Clear();
        MessageCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 點訊息跳轉時呼叫，回傳對應 NavigationTarget。
    /// OrbitSlot 來源：SourceId 應形如 "slot:N"（N=0..6），解析失敗時 SlotIndex=null（UI 不跳轉）。
    /// CompanionCard 來源：SourceId = companion id，直接傳給 LeftPanel。
    /// SystemHint / PendingTask 來源：無跳轉目標，回 Companion 但 TargetId 空（caller 應檢查）。
    /// </summary>
    public NavigationTarget RequestNavigation(MessageBubble bubble)
    {
        switch (bubble.Source)
        {
            case MessageBubbleSource.OrbitSlot:
                int? slotIndex = null;
                if (bubble.SourceId.StartsWith("slot:", StringComparison.Ordinal)
                    && int.TryParse(bubble.SourceId.AsSpan(5), out var n))
                    slotIndex = n;
                return new NavigationTarget(NavigationKind.Orbit, bubble.SourceId, slotIndex);
            case MessageBubbleSource.CompanionCard:
                return new NavigationTarget(NavigationKind.Companion, bubble.SourceId, null);
            default:
                return new NavigationTarget(NavigationKind.Companion, string.Empty, null);
        }
    }
}
