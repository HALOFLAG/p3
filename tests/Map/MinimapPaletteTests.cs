using CardNarrative.Core.Map;
using FluentAssertions;

namespace CardNarrative.Tests.Map;

/// <summary>
/// Phase 2 Task 10 — MinimapPalette L1 純函式測試（規格書 §2.3）。
/// </summary>
public class MinimapPaletteTests
{
    [Theory]
    [InlineData(MapTerrain.Forest,   0x3a, 0x68, 0x30)]
    [InlineData(MapTerrain.Path,     0xa8, 0x90, 0x4a)]
    [InlineData(MapTerrain.Grass,    0x5a, 0x98, 0x30)]
    [InlineData(MapTerrain.Water,    0x2a, 0x68, 0x98)]
    [InlineData(MapTerrain.Mountain, 0x78, 0x78, 0x78)]
    [InlineData(MapTerrain.Building, 0x8a, 0x50, 0x28)]
    public void GetTerrainColor_MatchesPalette(MapTerrain terrain, byte r, byte g, byte b)
    {
        var c = MinimapPalette.GetTerrainColor(terrain);
        c.R.Should().Be(r);
        c.G.Should().Be(g);
        c.B.Should().Be(b);
    }

    [Fact]
    public void ApplyExploredAdjustment_Explored_ReturnsOriginal()
    {
        var original = new MinimapPalette.Rgb(0x3a, 0x68, 0x30);
        var adjusted = MinimapPalette.ApplyExploredAdjustment(original, isExplored: true);
        adjusted.Should().Be(original);
    }

    [Fact]
    public void ApplyExploredAdjustment_Unexplored_30PercentBrightness()
    {
        var original = new MinimapPalette.Rgb(0x3a, 0x68, 0x30); // forest
        var adjusted = MinimapPalette.ApplyExploredAdjustment(original, isExplored: false);
        // 0x3a=58 ×0.3 = 17.4 → byte cast 17 (0x11)
        // 0x68=104 ×0.3 = 31.2 → 31 (0x1f)
        // 0x30=48 ×0.3 = 14.4 → 14 (0x0e)
        adjusted.R.Should().Be((byte)(0x3a * 0.3f));
        adjusted.G.Should().Be((byte)(0x68 * 0.3f));
        adjusted.B.Should().Be((byte)(0x30 * 0.3f));
    }

    [Fact]
    public void GetTileDisplayColor_NotPlaced_ReturnsNull()
    {
        var tile = new TileData(0, 0, MapTerrain.Forest, IsPlaced: false, IsExplored: false);
        MinimapPalette.GetTileDisplayColor(tile).Should().BeNull();
    }

    [Fact]
    public void GetTileDisplayColor_PlacedExplored_ReturnsTerrainColor()
    {
        var tile = new TileData(0, 0, MapTerrain.Water, IsPlaced: true, IsExplored: true);
        var c = MinimapPalette.GetTileDisplayColor(tile);
        c.Should().Be(new MinimapPalette.Rgb(0x2a, 0x68, 0x98));
    }

    [Fact]
    public void GetTileDisplayColor_PlacedUnexplored_Returns30PercentBrightness()
    {
        var tile = new TileData(0, 0, MapTerrain.Mountain, IsPlaced: true, IsExplored: false);
        var c = MinimapPalette.GetTileDisplayColor(tile);
        c.Should().Be(new MinimapPalette.Rgb(
            (byte)(0x78 * 0.3f),
            (byte)(0x78 * 0.3f),
            (byte)(0x78 * 0.3f)));
    }
}
