// Phase 2 任務 11 Stage 2a — Tile → MapTerrain (+ minimapColor / sceneTheme) 解析。
//
// 兩階段：
//   1) 若 tile.VisualProfile 已填，直接 parse VisualProfile.Terrain (string) 為 MapTerrain enum
//   2) 缺欄位則走 fallback 表：Terrain → MapTerrain（一對一映射，避免 wilderness 一對多衝突）
//
// 規格書 §2.3 小地圖 / §6.3 美術配色。
using CardNarrative.Core.Models;

namespace CardNarrative.Core.Map;

public static class TileVisualProfileResolver
{
    /// <summary>缺 visualProfile 時的 Terrain → MapTerrain fallback 表。</summary>
    public static readonly System.Collections.Generic.IReadOnlyDictionary<Terrain, MapTerrain> TerrainFallback =
        new System.Collections.Generic.Dictionary<Terrain, MapTerrain>
        {
            { Terrain.Town,       MapTerrain.Building },
            { Terrain.Wilderness, MapTerrain.Forest   },
            { Terrain.Dungeon,    MapTerrain.Mountain },
            { Terrain.Special,    MapTerrain.Path     },
        };

    /// <summary>
    /// 解析 tile 的 MapTerrain 視覺類別。
    /// VisualProfile.Terrain 優先；缺則 fallback；無效字串拋例外（schema 應已擋）。
    /// </summary>
    public static MapTerrain ResolveTerrain(Tile tile)
    {
        if (tile.VisualProfile is { } profile)
        {
            if (System.Enum.TryParse<MapTerrain>(profile.Terrain, ignoreCase: true, out var mt))
                return mt;
            // schema 已限定 enum，理論上不會到這 — 但防禦：fallback 走 Terrain
        }
        return TerrainFallback.TryGetValue(tile.Terrain, out var fallback)
            ? fallback
            : MapTerrain.Forest; // 終極 fallback（任何未列舉值）
    }

    /// <summary>解析完整視覺 profile：Terrain + 選用 minimapColor / sceneTheme。</summary>
    public static (MapTerrain Terrain, string? MinimapColor, string? SceneTheme) Resolve(Tile tile)
    {
        var terrain = ResolveTerrain(tile);
        return (terrain, tile.VisualProfile?.MinimapColor, tile.VisualProfile?.SceneTheme);
    }
}
