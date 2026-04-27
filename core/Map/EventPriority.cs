// Phase 2 任務 11 Stage 3.3 — WorldMap event emit 順序 priority
// （規格書 §3.4 / docs/任務11-地塊卡變化-分析.md §4 Stage 3 v3 修訂）。
//
// 原本 WorldMap 的事件發 emit 順序硬編碼在 mutation 內；改用 enum 讓 buffer flush
// 時依 priority 排序，可擴充：未來加 BuffApplied / StatusEffectChanged 直接挑空隙
// 數值（如 25, 75）即可，不必改舊順序。
//
// 數值間距 5/10 為新事件預留空間；UI 訂閱通常依此順序處理：
//   - 視覺層（TileChanged → PlayerMoved）先
//   - HUD 層（HpChanged / ApChanged / HandChanged）次
//   - Mode / Companion 末
//   - Tile 變化後事件（TileTransformed）最末
namespace CardNarrative.Core.Map;

public enum EventPriority
{
    TileChanged = 10,
    TilePlaced = 15,
    PlayerMoved = 20,
    HpChanged = 30,
    ApChanged = 35,
    HandChanged = 40,
    HandSizeChanged = 45,
    ModeChanged = 50,
    /// <summary>PR-A · 裝備配置變更（grantEquipment effect / MoveEquipment* 後）。</summary>
    EquipmentChanged = 55,
    CompanionChanged = 60,
    /// <summary>Stage 5 transformTile 事件後的對應 emit（高於一般 TileChanged）。</summary>
    TileTransformed = 70,
}
