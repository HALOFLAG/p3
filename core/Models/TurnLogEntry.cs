// TurnLogEntry — 單筆回合紀錄（文字 + 色調分類），由 TurnLogService 收集供 UI 顯示。
// 提供 IsHit / IsMiss / IsNeutral 便利屬性供 XAML Classes.* 觸發。
namespace CardNarrative.Core.Models;

public enum TurnLogKind
{
    Neutral,
    Hit,
    Miss
}

public sealed record TurnLogEntry(string Text, TurnLogKind Kind, int BigRound, int SubTurn)
{
    public bool IsHit => Kind == TurnLogKind.Hit;
    public bool IsMiss => Kind == TurnLogKind.Miss;
    public bool IsNeutral => Kind == TurnLogKind.Neutral;
}
