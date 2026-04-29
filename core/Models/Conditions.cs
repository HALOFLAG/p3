// Conditions — 勝負條件與事件觸發條件定義（多型 record）。
// WinCondition：FlagWinCondition。
// LoseCondition：TurnLimit / AllPlayersDown。
// EventTrigger（S2 起）：
//   - TileEnter         地塊進入時觸發
//   - Flag              指定 flag 達成時觸發
//   - TurnAt            第 N 個大回合行動階段觸發（一次性）
//   - TurnRange         在 [From, To] 區間的大回合中皆可觸發；To 省略 = 至遊戲結束
//   - PlayerAction      玩家執行特定動作（Move/Rest/Observe/Talk/...）時觸發；可選 Count
//   - TurnTimer         [Obsolete] 每 N 回合循環（runtime 忽略，反序列化保留）
//   - ActionCount       [Obsolete] 總行動次數（runtime 忽略，反序列化保留）
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardNarrative.Core.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FlagWinCondition), "flag")]
public abstract record WinCondition;

public sealed record FlagWinCondition(string Key, JsonElement Value) : WinCondition;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TurnLimitLoseCondition),     "turnLimit")]
[JsonDerivedType(typeof(AllPlayersDownLoseCondition), "allPlayersDown")]
public abstract record LoseCondition;

public sealed record TurnLimitLoseCondition(int Value) : LoseCondition;
public sealed record AllPlayersDownLoseCondition() : LoseCondition;

#pragma warning disable CS0618 // 反序列化登記必須照舊覆蓋 obsolete trigger 型別
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TileEnterTrigger),     "tileEnter")]
[JsonDerivedType(typeof(FlagTrigger),          "flag")]
[JsonDerivedType(typeof(TurnAtTrigger),        "turnAt")]
[JsonDerivedType(typeof(TurnRangeTrigger),     "turnRange")]
[JsonDerivedType(typeof(PlayerActionTrigger),  "playerAction")]
[JsonDerivedType(typeof(TurnTimerTrigger),     "turnTimer")]
[JsonDerivedType(typeof(ActionCountTrigger),   "actionCount")]
public abstract record EventTrigger;
#pragma warning restore CS0618

public sealed record TileEnterTrigger(string TileId) : EventTrigger;
public sealed record FlagTrigger(string Key, JsonElement Value) : EventTrigger;

/// <summary>第 <paramref name="Round"/> 個大回合行動階段開始時觸發（一次性）。</summary>
public sealed record TurnAtTrigger(int Round) : EventTrigger;

/// <summary>
/// 在大回合區間 [From, To] 行動階段開始時觸發；
/// <paramref name="To"/> 為 null 表示「自 From 起至遊戲結束皆可觸發」。
/// 範圍語義為閉區間（含端點）。
/// </summary>
public sealed record TurnRangeTrigger(int From, int? To) : EventTrigger;

/// <summary>
/// 玩家執行 <paramref name="Kind"/> 動作時觸發。
/// <paramref name="Count"/> 為 null：每次該動作皆嘗試觸發（一旦命中且事件被消費則自動結束）。
/// <paramref name="Count"/> 為正整數：累積到第 N 次該動作時才觸發（讀 GameState.ActionCounts[Kind]）。
/// </summary>
public sealed record PlayerActionTrigger(PlayerActionKind Kind, int? Count) : EventTrigger;

/// <summary>[Obsolete] 每 N 回合循環。S2 起 EventBroker 不再評估此型；反序列化保留供舊存檔載入。</summary>
[Obsolete("Use TurnAtTrigger or TurnRangeTrigger instead. Runtime ignores this trigger as of S2.")]
public sealed record TurnTimerTrigger(int Interval) : EventTrigger;

/// <summary>[Obsolete] 總行動次數累計。S2 起 EventBroker 不再評估此型；反序列化保留供舊存檔載入。</summary>
[Obsolete("Use PlayerActionTrigger with Count instead. Runtime ignores this trigger as of S2.")]
public sealed record ActionCountTrigger(int Threshold) : EventTrigger;
