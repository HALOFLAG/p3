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
)
{
    /// <summary>
    /// Phase 3 任務 14（S4）· 模組情報定義。intel.json 為選用檔；缺檔 = 空 dict。
    /// 與 <see cref="State.GameState.AcquiredIntel"/> 配合：玩家取得後加入 set，
    /// JsonLogic context 對外曝為 hasIntel.&lt;id&gt;；UnlocksTags 影響 TileDeckService 過濾。
    /// </summary>
    public IReadOnlyDictionary<string, Intel> Intel { get; init; } =
        new Dictionary<string, Intel>(System.StringComparer.Ordinal);
}
