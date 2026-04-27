// MessageBubble — 訊息氣泡單筆資料（規格書 §1.9）。
// 由 MessageBubbleService 收集；UI 顯示為摺疊圓點 + 點開列表，TurnEnd 清空。
// IsImportant = true 時 UI 紅色高亮（Phase 2 僅 OrbitSlot 結算成功/失敗 = true）。
// SourceId 用於 RequestNavigation：OrbitSlot → orbit 槽位 id；CompanionCard → companion id。
namespace CardNarrative.Core.Models;

public enum MessageBubbleSource
{
    OrbitSlot,
    CompanionCard,
    SystemHint,
    PendingTask,
}

public sealed record MessageBubble(
    int Id,
    string Text,
    MessageBubbleSource Source,
    string SourceId,
    DateTime Timestamp,
    bool IsImportant);
