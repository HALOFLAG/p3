// Conditions — 勝負條件與事件觸發條件定義（多型 record）。
// WinCondition：FlagWinCondition。
// LoseCondition：TurnLimit / AllPlayersDown。
// EventTrigger：TileEnter / Flag / TurnTimer / ActionCount，由 EventScheduler 搭配 TurnLoop 判斷。
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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TileEnterTrigger),   "tileEnter")]
[JsonDerivedType(typeof(FlagTrigger),        "flag")]
[JsonDerivedType(typeof(TurnTimerTrigger),   "turnTimer")]
[JsonDerivedType(typeof(ActionCountTrigger), "actionCount")]
public abstract record EventTrigger;

public sealed record TileEnterTrigger(string TileId) : EventTrigger;
public sealed record FlagTrigger(string Key, JsonElement Value) : EventTrigger;
public sealed record TurnTimerTrigger(int Interval) : EventTrigger;
public sealed record ActionCountTrigger(int Threshold) : EventTrigger;
