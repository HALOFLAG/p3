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
