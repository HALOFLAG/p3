namespace CardNarrative.Core.Map;

/// <summary>
/// Phase 1 Task 2 — 地圖渲染用的地形分類。
/// 與 Models.Terrain（Town/Wilderness/Dungeon/Special）刻意不同，
/// 對應規格書 §5.3.2 / §6.3 的 6 種地塊配色。
/// 未來 Phase 2 整合 Tile/TileCard 時會新增 MapTerrain ↔ Terrain 的橋接。
/// </summary>
public enum MapTerrain
{
    Forest,
    Path,
    Grass,
    Water,
    Mountain,
    Building,
}
