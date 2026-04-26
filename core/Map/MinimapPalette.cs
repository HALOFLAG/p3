namespace CardNarrative.Core.Map;

/// <summary>
/// Phase 2 Task 10 — 小地圖地形配色（規格書 §2.3 / §6.1）。
///
/// 純 C# 純函式工具，無 Godot 依賴；可直接在 xUnit 測試與 Godot 端共用。
/// Godot 端用 <c>Color.Color8(c.R, c.G, c.B)</c> 轉成 <c>Godot.Color</c>。
/// </summary>
public static class MinimapPalette
{
    public readonly record struct Rgb(byte R, byte G, byte B);

    /// <summary>未探索亮度比例（規格書 §2.3：未探索 ×30% 亮度）。</summary>
    public const float UnexploredBrightness = 0.3f;

    /// <summary>規格書 §2.3 配色：forest #3a6830 / path #a8904a / grass #5a9830 / water #2a6898 / mountain #787878 / building #8a5028。</summary>
    public static Rgb GetTerrainColor(MapTerrain terrain) => terrain switch
    {
        MapTerrain.Forest   => new Rgb(0x3a, 0x68, 0x30),
        MapTerrain.Path     => new Rgb(0xa8, 0x90, 0x4a),
        MapTerrain.Grass    => new Rgb(0x5a, 0x98, 0x30),
        MapTerrain.Water    => new Rgb(0x2a, 0x68, 0x98),
        MapTerrain.Mountain => new Rgb(0x78, 0x78, 0x78),
        MapTerrain.Building => new Rgb(0x8a, 0x50, 0x28),
        _ => new Rgb(0, 0, 0),
    };

    /// <summary>探索狀態調整：未探索 ×30% 亮度；已探索維持原色。</summary>
    public static Rgb ApplyExploredAdjustment(Rgb color, bool isExplored)
    {
        if (isExplored) return color;
        return new Rgb(
            (byte)(color.R * UnexploredBrightness),
            (byte)(color.G * UnexploredBrightness),
            (byte)(color.B * UnexploredBrightness));
    }

    /// <summary>給定 tile，回傳該繪在小地圖上的顏色。未放置 tile 回 null（不繪）。</summary>
    public static Rgb? GetTileDisplayColor(TileData tile)
    {
        if (!tile.IsPlaced) return null;
        return ApplyExploredAdjustment(GetTerrainColor(tile.Terrain), tile.IsExplored);
    }
}
