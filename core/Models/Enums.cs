// Enums — 全專案共用列舉（皆以 JsonStringEnumConverter 序列化）。
// ActionType、Terrain、Stat、EquipmentSlot、ItemCategory、EndingGrade、
// EventType、ExplorationLevel（-2~+2）、ProgressReason（地塊推進來源）。
using System.Text.Json.Serialization;

namespace CardNarrative.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    Communication,
    Combat,
    Exploration,
    Thinking
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Terrain
{
    Town,
    Wilderness,
    Dungeon,
    Special
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Stat
{
    Power,
    Social,
    Skill,
    Intellect
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EquipmentSlot
{
    Weapon,
    OffHand,
    Head,
    Body,
    Hand,
    Feet,
    AccessoryA,
    AccessoryB,
    Utility,
    /// <summary>
    /// Legacy sentinel: prior version stored CharacterCardSlot=Character to mean
    /// "character card lives in a non-equipment slot." Current design places the
    /// character card directly into one of the visible primary slots (default Head).
    /// Retained so older saves deserialize without error; PlayerState should never
    /// emit this value going forward.
    /// </summary>
    [Obsolete("Character card now occupies a regular primary slot; default to Head.")]
    Character
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemCategory
{
    Weapon,
    Armor,
    Accessory,
    Special
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EndingGrade
{
    S,
    A,
    B,
    C
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventType
{
    Exploration,
    Negotiation,
    Battle,
    Special
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExplorationLevel
{
    Unknown = -2,
    Unfamiliar = -1,
    Neutral = 0,
    Familiar = 1,
    Mastered = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProgressReason
{
    FirstEnter,
    ActionCard,
    EventOutcome,
    ClueInvestment
}

/// <summary>
/// Phase 3 任務 14（S2）· 玩家動作 verb — 與 <see cref="ActionType"/>（行動卡分類）不同；
/// 此 enum 是「玩家在行動階段做了什麼」的細粒度分類，供 <see cref="EventTrigger"/>
/// 的 PlayerActionTrigger 命中判斷與 <see cref="State.GameState.ActionCounts"/> 累積使用。
///
/// JSON 序列化採小寫字串（playerAction trigger 寫 "kind": "observe" 等）。
/// **未知 kind 反序列化時 fallback 為 <see cref="Interact"/> + warn log**，避免新增 verb
/// 時老版本 client 反序列化炸；模組作者文件需明確列出已支援 kind。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayerActionKind
{
    /// <summary>移動：WorldMap.TryMovePlayerTo / TurnLoop.Move。</summary>
    Move,
    /// <summary>休息：TurnLoop.Rest / WorldMap.Rest。</summary>
    Rest,
    /// <summary>觀察（"explore"）：WorldMap.Observe。</summary>
    Observe,
    /// <summary>對話（"talk"）：TileInteraction Communication 類。</summary>
    Talk,
    /// <summary>出牌：TurnLoop.PlayCard。</summary>
    PlayCard,
    /// <summary>整理手牌：TurnLoop.Mulligan。</summary>
    Mulligan,
    /// <summary>專注：TurnLoop.Focus。</summary>
    Focus,
    /// <summary>偵察：TurnLoop.Scout。</summary>
    Scout,
    /// <summary>採集資源：TurnLoop.CollectResource。</summary>
    CollectResource,
    /// <summary>投入線索推進地塊：TurnLoop.InvestCluesForProgress。</summary>
    InvestClue,
    /// <summary>交換裝備：TurnLoop.SwapEquipment。</summary>
    SwapEquipment,
    /// <summary>地塊互動（fallback：未知 kind 也歸此類）：TileInteractionService.Execute 非 Communication 類。</summary>
    Interact,
}
