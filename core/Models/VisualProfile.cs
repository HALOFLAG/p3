// Phase 2 任務 11 Stage 2a — Tile 視覺對應 profile（規格書 §2.3 / §6.3）。
//
// 動機：terrain enum (Town/Wilderness/Dungeon/Special) 是「遊戲語意」分類，
//       MapTerrain enum (Forest/Path/Grass/Water/Mountain/Building) 是「視覺顏色」分類，
//       一對多衝突 — 例：wilderness 可能同時涵蓋 forest / swamp / desert，
//       光靠 terrain string 無法決定地圖上該渲染哪種色塊。
//
// 解法：每個 tile 在 tile.json 加 visualProfile 自帶視覺 metadata；
//       runtime 透過 TileVisualProfileResolver 把 visualProfile.terrain
//       (string) 解析為 MapTerrain enum，缺欄位時 fallback 走 terrain → MapTerrain 表。
namespace CardNarrative.Core.Models;

public sealed record VisualProfile(
    string Terrain,
    string? MinimapColor = null,
    string? SceneTheme = null);
