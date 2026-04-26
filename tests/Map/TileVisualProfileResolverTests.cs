// Phase 2 任務 11 Stage 2a — TileVisualProfileResolver 測試。
// 驗證：
// - VisualProfile 已填時優先採用，並把 string 解析為 MapTerrain enum
// - 缺 VisualProfile 時走 Terrain → MapTerrain fallback 表
// - 大小寫不敏感（"forest" / "Forest" 都接受）
// - 無效字串仍走 fallback 不拋例外
// - abandoned-mansion 19 個 tile 全部能成功 resolve
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class TileVisualProfileResolverTests
{
    private static Tile MakeTile(Terrain terrain, VisualProfile? profile = null) =>
        new Tile(
            Id: "t1",
            Name: "T1",
            Terrain: terrain,
            Important: false,
            AllowedActionTypes: System.Array.Empty<ActionType>(),
            Resources: System.Array.Empty<TileResource>(),
            OnEnter: System.Array.Empty<EffectBase>())
        {
            VisualProfile = profile
        };

    // === Fallback table ===

    [Theory]
    [InlineData(Terrain.Town, MapTerrain.Building)]
    [InlineData(Terrain.Wilderness, MapTerrain.Forest)]
    [InlineData(Terrain.Dungeon, MapTerrain.Mountain)]
    [InlineData(Terrain.Special, MapTerrain.Path)]
    public void ResolveTerrain_NoVisualProfile_UsesFallback(Terrain terrain, MapTerrain expected)
    {
        var tile = MakeTile(terrain);
        TileVisualProfileResolver.ResolveTerrain(tile).Should().Be(expected);
    }

    // === VisualProfile precedence ===

    [Theory]
    [InlineData("Forest", MapTerrain.Forest)]
    [InlineData("Path", MapTerrain.Path)]
    [InlineData("Grass", MapTerrain.Grass)]
    [InlineData("Water", MapTerrain.Water)]
    [InlineData("Mountain", MapTerrain.Mountain)]
    [InlineData("Building", MapTerrain.Building)]
    public void ResolveTerrain_VisualProfile_OverridesFallback(string profileTerrain, MapTerrain expected)
    {
        // Tile 標 Terrain.Town 但 VisualProfile 寫 Forest → 應採 VisualProfile
        var tile = MakeTile(Terrain.Town, new VisualProfile(profileTerrain));
        TileVisualProfileResolver.ResolveTerrain(tile).Should().Be(expected);
    }

    [Fact]
    public void ResolveTerrain_VisualProfileLowercase_ParseSucceedsCaseInsensitive()
    {
        var tile = MakeTile(Terrain.Town, new VisualProfile("water"));
        TileVisualProfileResolver.ResolveTerrain(tile).Should().Be(MapTerrain.Water);
    }

    [Fact]
    public void ResolveTerrain_VisualProfileInvalid_FallsBackToTerrain()
    {
        // schema 應已擋；但 runtime 防禦 — 無效字串走 Terrain fallback
        var tile = MakeTile(Terrain.Town, new VisualProfile("InvalidValue"));
        TileVisualProfileResolver.ResolveTerrain(tile).Should().Be(MapTerrain.Building);
    }

    [Fact]
    public void Resolve_ReturnsAllProfileFields()
    {
        var tile = MakeTile(Terrain.Town, new VisualProfile("Forest", "#3a6830", "forest-day"));
        var result = TileVisualProfileResolver.Resolve(tile);
        result.Terrain.Should().Be(MapTerrain.Forest);
        result.MinimapColor.Should().Be("#3a6830");
        result.SceneTheme.Should().Be("forest-day");
    }

    [Fact]
    public void Resolve_NoProfile_OptionalFieldsAreNull()
    {
        var tile = MakeTile(Terrain.Wilderness);
        var result = TileVisualProfileResolver.Resolve(tile);
        result.Terrain.Should().Be(MapTerrain.Forest);
        result.MinimapColor.Should().BeNull();
        result.SceneTheme.Should().BeNull();
    }

    // === abandoned-mansion module integration ===

    [Fact]
    public void AbandonedMansion_AllTilesHaveVisualProfile()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.AbandonedMansionFolder);
        var module = ((ModuleLoadResult.Success)result).Module;

        // Stage 2a 拍板：精細路線 — 19 tile 全填
        module.Tiles.Values.Should().HaveCount(19);
        module.Tiles.Values.Should().AllSatisfy(t => t.VisualProfile.Should().NotBeNull(
            $"tile '{t.Id}' should have visualProfile per Stage 2a"));
    }

    [Fact]
    public void AbandonedMansion_AllTilesResolveSuccessfully()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.AbandonedMansionFolder);
        var module = ((ModuleLoadResult.Success)result).Module;

        foreach (var tile in module.Tiles.Values)
        {
            var act = () => TileVisualProfileResolver.Resolve(tile);
            act.Should().NotThrow($"tile '{tile.Id}' should resolve");
        }
    }

    [Theory]
    [InlineData("village-square", MapTerrain.Building)]
    [InlineData("forest-path", MapTerrain.Forest)]
    [InlineData("mansion-front-yard", MapTerrain.Grass)]
    [InlineData("damp-stone-stairs", MapTerrain.Water)]
    [InlineData("basement-entry", MapTerrain.Mountain)]
    [InlineData("grand-hallway", MapTerrain.Path)]
    public void AbandonedMansion_SpecificTilesMapToExpectedTerrain(string tileId, MapTerrain expected)
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.AbandonedMansionFolder);
        var module = ((ModuleLoadResult.Success)result).Module;

        module.Tiles.Should().ContainKey(tileId);
        var tile = module.Tiles[tileId];
        TileVisualProfileResolver.ResolveTerrain(tile).Should().Be(expected);
    }
}
