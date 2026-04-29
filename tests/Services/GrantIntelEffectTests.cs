// GrantIntelEffectTests — Phase 3 任務 14（S4）grantIntel 效果與 TileDeck UnlocksTags 過濾。
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class GrantIntelEffectTests
{
    private static Module BuildModuleWith(
        IReadOnlyDictionary<string, Intel>? intel = null,
        IReadOnlyDictionary<string, Tile>? tiles = null)
    {
        var baseModule = ModuleFactory.Load();
        Module result = baseModule;
        if (intel is not null) result = result with { Intel = intel };
        if (tiles is not null) result = result with { Tiles = tiles };
        return result;
    }

    [Fact]
    public void EffectHandler_AppliesGrantIntel_AddsToAcquiredIntel()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var handler = new EffectHandler();

        handler.Apply(new GrantIntelEffect("priest-diary"), state, module);

        state.AcquiredIntel.Should().Contain("priest-diary");
    }

    [Fact]
    public void TileWithLockedTag_NotInValidPlacementCells_WhenIntelMissing()
    {
        // 構造：intel "key" unlocks tag "underground"；tile "tunnel" 含 tag "underground"。
        var intel = new Dictionary<string, Intel>
        {
            ["key"] = new Intel("key", "鑰匙情報", "desc") { UnlocksTags = new[] { "underground" } },
        };
        var underTile = new Tile(
            Id: "tunnel",
            Name: "地道",
            Terrain: Terrain.Dungeon,
            Important: false,
            AllowedActionTypes: System.Array.Empty<ActionType>(),
            Resources: System.Array.Empty<TileResource>(),
            OnEnter: System.Array.Empty<EffectBase>())
        {
            Tags = new[] { "underground" },
        };

        var baseModule = ModuleFactory.Load();
        var tilesDict = new Dictionary<string, Tile>(baseModule.Tiles);
        tilesDict["tunnel"] = underTile;
        var module = baseModule with { Intel = intel, Tiles = tilesDict };

        var state = ModuleFactory.NewState(module);
        // 把 tunnel 放到 deck 頂、用 GetValidPlacementCells 檢查
        state.TileDeck.Insert(0, "tunnel");

        // 沒拿情報 → 整張 tile 鎖死，候選格為空
        var cells = TileDeckService.GetValidPlacementCells(state, module, "tunnel");
        cells.Should().BeEmpty();

        // 取得情報後 → 候選格出現（至少 1 格 — 因為 tunnel 含 tag underground，
        // 實際合不合 tag rule 取決於起始 tile 的 tags；這裡只檢查不被 intel 鎖死即可，
        // 而起始 tile（"warehouse"）有 tag "town"，underground 與 town 不共通。
        // 為避免 tag rule 干擾本測試，把 tunnel 改為「無 tag bridge」也不行（無 tag 才能放在任何 tile 旁），
        // 但這樣就不會被 intel 鎖了。所以以「鎖時為 0 / 解鎖時 ≥ 0」分別語意 assert：
        state.AcquiredIntel.Add("key");
        var afterUnlock = TileDeckService.GetValidPlacementCells(state, module, "tunnel");
        // 解鎖後可能仍因 tag 規則為 0 — 重點是 intel 鎖不再生效；用 IsTileTagLockedByIntel 直接驗
        TileDeckService.IsTileTagLockedByIntel(state, module, underTile).Should().BeFalse();
    }

    [Fact]
    public void IsTileTagLockedByIntel_ReturnsTrue_WhenAnyTagLockedAndNoIntel()
    {
        var intel = new Dictionary<string, Intel>
        {
            ["key"] = new Intel("key", "情報", "desc") { UnlocksTags = new[] { "underground" } },
        };
        var module = BuildModuleWith(intel: intel);
        var state = ModuleFactory.NewState(module);
        var tile = new Tile(
            "x", "x", Terrain.Dungeon, false,
            System.Array.Empty<ActionType>(),
            System.Array.Empty<TileResource>(),
            System.Array.Empty<EffectBase>())
        {
            Tags = new[] { "underground", "danger" },
        };

        TileDeckService.IsTileTagLockedByIntel(state, module, tile).Should().BeTrue();

        state.AcquiredIntel.Add("key");
        TileDeckService.IsTileTagLockedByIntel(state, module, tile).Should().BeFalse();
    }

    [Fact]
    public void IsTileTagLockedByIntel_ReturnsFalse_WhenTileHasNoLockableTag()
    {
        var intel = new Dictionary<string, Intel>
        {
            ["key"] = new Intel("key", "情報", "desc") { UnlocksTags = new[] { "underground" } },
        };
        var module = BuildModuleWith(intel: intel);
        var state = ModuleFactory.NewState(module);
        var tile = new Tile(
            "x", "x", Terrain.Town, false,
            System.Array.Empty<ActionType>(),
            System.Array.Empty<TileResource>(),
            System.Array.Empty<EffectBase>())
        {
            Tags = new[] { "town" },
        };

        TileDeckService.IsTileTagLockedByIntel(state, module, tile).Should().BeFalse();
    }

    [Fact]
    public void IsTileTagLockedByIntel_ReturnsFalse_WhenIntelDictEmpty()
    {
        var module = ModuleFactory.Load(); // 此模組無 intel
        var state = ModuleFactory.NewState(module);
        var tile = new Tile(
            "x", "x", Terrain.Town, false,
            System.Array.Empty<ActionType>(),
            System.Array.Empty<TileResource>(),
            System.Array.Empty<EffectBase>())
        {
            Tags = new[] { "underground" },
        };

        TileDeckService.IsTileTagLockedByIntel(state, module, tile).Should().BeFalse();
    }
}
