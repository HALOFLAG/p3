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
