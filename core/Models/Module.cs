// Module — 載入後的模組聚合（所有 JSON 資料的唯讀快照）。
// 字典 key = Id；由 ModuleLoader 產出，供 TurnLoop/EventResolver/BattleEngine 查表。
namespace CardNarrative.Core.Models;

public sealed record Module(
    Manifest Manifest,
    Prologue Prologue,
    IReadOnlyDictionary<string, Character> Characters,
    IReadOnlyDictionary<string, NpcCompanion> NpcCompanions,
    IReadOnlyDictionary<string, Tile> Tiles,
    IReadOnlyDictionary<string, EventCard> Events,
    IReadOnlyDictionary<string, ActionCard> ActionCards,
    IReadOnlyDictionary<string, Equipment> Equipment,
    IReadOnlyDictionary<string, Ending> Endings,
    IReadOnlyDictionary<string, BattleCard> Battles
);
