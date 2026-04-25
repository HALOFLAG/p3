using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class TileDeckServiceTests
{
    [Fact]
    public void Deck_IsDeterministicForSameSeed()
    {
        var module = ModuleFactory.Load();
        var s1 = ModuleFactory.NewState(module, seed: 42);
        var s2 = ModuleFactory.NewState(module, seed: 42);
        s1.TileDeck.Should().Equal(s2.TileDeck);
    }

    [Fact]
    public void Deck_ExcludesStartingTile_AndMayDifferAcrossSeeds()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, seed: 1);
        state.TileDeck.Should().NotContain(module.Prologue.StartingTileId);
        state.TileDeck.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Draw_RemovesTopOfDeck()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, seed: 7);
        var top = state.TileDeck[0];
        var drawn = TileDeckService.Draw(state);
        drawn.Should().Be(top);
        state.TileDeck.Should().NotContain(top).And.HaveCount(module.Tiles.Count - 2);
    }

    [Fact]
    public void Draw_ReturnsNull_WhenEmpty()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileDeck.Clear();
        TileDeckService.Draw(state).Should().BeNull();
    }

    [Fact]
    public void ValidCells_ReturnsAdjacent_MinusOccupied()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        // starting tile at (0,0); add one east so (1,0) is occupied.
        state.TileMap[(1, 0)] = new PlacedTile { TileId = "warehouse" };

        var cells = TileDeckService.GetValidPlacementCells(state).Select(p => (p.X, p.Y)).ToHashSet();
        cells.Should().Contain((-1, 0));
        cells.Should().Contain((0, 1));
        cells.Should().Contain((0, -1));
        cells.Should().Contain((2, 0));
        cells.Should().Contain((1, 1));
        cells.Should().Contain((1, -1));
        cells.Should().NotContain((0, 0));
        cells.Should().NotContain((1, 0));
    }

    [Fact]
    public void Place_RejectsNonAdjacent()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var tid = state.TileDeck[0];
        var act = () => TileDeckService.Place(state, module, tid, new Position(5, 5));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Place_RejectsOccupied()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var tid = state.TileDeck[0];
        var act = () => TileDeckService.Place(state, module, tid, new Position(0, 0));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Place_AddsToTileMap_AndRemovesFromDeck()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var tid = state.TileDeck[0];
        int before = state.TileDeck.Count;

        TileDeckService.Place(state, module, tid, new Position(1, 0));

        state.TileMap.Should().ContainKey((1, 0));
        state.TileMap[(1, 0)].TileId.Should().Be(tid);
        state.TileMap[(1, 0)].Level.Should().Be(ExplorationLevel.Unknown);
        state.TileDeck.Should().HaveCount(before - 1);
    }

    // ── Tag-based placement (plan B) ──────────────────────────────

    // Build a tagged tile dict on top of a loaded module (keeps manifest, prologue, etc.).
    private static Module ModuleWithTaggedTiles()
    {
        var baseModule = ModuleFactory.Load();
        Tile T(string id, params string[] tags) => new(
            Id: id, Name: id, Terrain: Terrain.Wilderness, Important: false,
            AllowedActionTypes: Array.Empty<ActionType>(),
            Resources: Array.Empty<TileResource>(),
            OnEnter: Array.Empty<EffectBase>()
        ) { Tags = tags };

        var tiles = new Dictionary<string, Tile>
        {
            ["outA"] = T("outA", "outdoor"),
            ["outB"] = T("outB", "outdoor"),
            ["gateway"] = T("gateway", "outdoor", "indoor"),
            ["indoorA"] = T("indoorA", "indoor"),
            ["neutral"] = T("neutral"),
        };
        return baseModule with { Tiles = tiles };
    }

    private static GameState StateWithMap(Module module, params (string tileId, int x, int y)[] placed)
    {
        var state = new GameState { RngSeed = 1, MaxBigRounds = 5 };
        foreach (var (tid, x, y) in placed)
            state.TileMap[(x, y)] = new PlacedTile { TileId = tid };
        return state;
    }

    [Fact]
    public void TagRule_MatchingTag_Allowed()
    {
        var m = ModuleWithTaggedTiles();
        var s = StateWithMap(m, ("outA", 0, 0));
        s.TileDeck.Add("outB");
        TileDeckService.IsTagPlacementAllowed(s, m, "outB", new Position(1, 0)).Should().BeTrue();
    }

    [Fact]
    public void TagRule_DisjointTags_Rejected()
    {
        var m = ModuleWithTaggedTiles();
        var s = StateWithMap(m, ("outA", 0, 0));
        s.TileDeck.Add("indoorA");
        TileDeckService.IsTagPlacementAllowed(s, m, "indoorA", new Position(1, 0)).Should().BeFalse();
    }

    [Fact]
    public void TagRule_EmptyTagsCandidate_AllowsAnywhereAdjacent()
    {
        var m = ModuleWithTaggedTiles();
        var s = StateWithMap(m, ("indoorA", 0, 0));
        s.TileDeck.Add("neutral");
        TileDeckService.IsTagPlacementAllowed(s, m, "neutral", new Position(1, 0)).Should().BeTrue();
    }

    [Fact]
    public void TagRule_EmptyTagsNeighbor_AcceptsAnyCandidate()
    {
        var m = ModuleWithTaggedTiles();
        var s = StateWithMap(m, ("neutral", 0, 0));
        s.TileDeck.Add("indoorA");
        TileDeckService.IsTagPlacementAllowed(s, m, "indoorA", new Position(1, 0)).Should().BeTrue();
    }

    [Fact]
    public void TagRule_Gateway_BridgesTwoClusters()
    {
        var m = ModuleWithTaggedTiles();
        // outA(0,0) — gateway can be placed adjacent because it has "outdoor"
        var s = StateWithMap(m, ("outA", 0, 0));
        s.TileDeck.Add("gateway");
        TileDeckService.IsTagPlacementAllowed(s, m, "gateway", new Position(1, 0)).Should().BeTrue();

        // Now map: outA(0,0), gateway(1,0). indoorA next to gateway should be allowed.
        s.TileMap[(1, 0)] = new PlacedTile { TileId = "gateway" };
        TileDeckService.IsTagPlacementAllowed(s, m, "indoorA", new Position(2, 0)).Should().BeTrue();
        // But indoorA next to outA alone should fail.
        TileDeckService.IsTagPlacementAllowed(s, m, "indoorA", new Position(0, 1)).Should().BeFalse();
    }

    [Fact]
    public void Place_ThrowsWhenTagRuleRejects()
    {
        var m = ModuleWithTaggedTiles();
        var s = StateWithMap(m, ("outA", 0, 0));
        s.TileDeck.Add("indoorA");
        var act = () => TileDeckService.Place(s, m, "indoorA", new Position(1, 0));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetValidPlacementCells_FiltersByTagRule()
    {
        var m = ModuleWithTaggedTiles();
        var s = StateWithMap(m, ("outA", 0, 0));
        s.TileDeck.Add("indoorA");
        // Structural cells around (0,0): (1,0) (-1,0) (0,1) (0,-1). None of them allow indoorA.
        TileDeckService.GetValidPlacementCells(s, m, "indoorA").Should().BeEmpty();
        // But a neutral tile fits all 4.
        s.TileDeck.Clear(); s.TileDeck.Add("neutral");
        TileDeckService.GetValidPlacementCells(s, m, "neutral").Should().HaveCount(4);
    }
}
