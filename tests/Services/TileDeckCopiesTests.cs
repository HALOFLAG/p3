using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

/// <summary>
/// Multi-copy corridor tiles: a tile with Copies = N appears N times back-to-back
/// in the deck, and the 2nd…Nth placements must be orthogonally adjacent to an
/// already-placed copy (forms a 2x1 / 3x1 corridor on the board).
/// </summary>
public class TileDeckCopiesTests
{
    private const string StartId = "start";
    private const string CorridorId = "corridor";
    private const string FillerId = "filler";

    private static Module BuildModule(int corridorCopies = 2)
    {
        var manifest = new Manifest("test", "Test", "1.0", "", 1, "", "");
        var prologue = new Prologue(
            "Test", 1, "",
            new List<WinCondition>(),
            new List<LoseCondition>(),
            new DifficultyCurve(
                new DifficultyRange(new[] { 8, 10 }),
                new DifficultyRange(new[] { 10, 12 }),
                new ClimaxCurve(14, 1, 18)),
            StartId,
            Array.Empty<string>(),
            0,
            "contributions * 2 + equipmentSlotsFilled");
        var tiles = new Dictionary<string, Tile>
        {
            [StartId] = new Tile(StartId, "Start", Terrain.Town, false,
                Array.Empty<ActionType>(), Array.Empty<TileResource>(), Array.Empty<EffectBase>()),
            [CorridorId] = new Tile(CorridorId, "Corridor", Terrain.Wilderness, false,
                Array.Empty<ActionType>(), Array.Empty<TileResource>(), Array.Empty<EffectBase>())
                { Copies = corridorCopies },
            [FillerId] = new Tile(FillerId, "Filler", Terrain.Town, false,
                Array.Empty<ActionType>(), Array.Empty<TileResource>(), Array.Empty<EffectBase>()),
        };
        var characters = new Dictionary<string, Character>
        {
            ["hero"] = new Character("hero", "Hero",
                new StatBlock(1, 1, 1, 1), 10, "",
                Array.Empty<string>()),
        };
        return new Module(
            manifest, prologue, characters,
            new Dictionary<string, NpcCompanion>(),
            tiles,
            new Dictionary<string, EventCard>(),
            new Dictionary<string, ActionCard>(),
            new Dictionary<string, Equipment>(),
            new Dictionary<string, Ending>(),
            new Dictionary<string, BattleCard>());
    }

    private static GameState NewState(Module module, int seed = 1)
        => GameState.CreateNew(module, new[] { "hero" }, Array.Empty<string>(), seed);

    [Fact]
    public void Deck_ContainsNCopies_WhenTileHasCopiesN()
    {
        var module = BuildModule(corridorCopies: 2);
        var state = NewState(module);
        state.TileDeck.Count(id => id == CorridorId).Should().Be(2);
    }

    [Fact]
    public void Deck_SeedsSameIdConsecutively()
    {
        var module = BuildModule(corridorCopies: 3);
        var state = NewState(module);
        int firstIdx = state.TileDeck.IndexOf(CorridorId);
        firstIdx.Should().BeGreaterOrEqualTo(0);
        state.TileDeck[firstIdx].Should().Be(CorridorId);
        state.TileDeck[firstIdx + 1].Should().Be(CorridorId);
        state.TileDeck[firstIdx + 2].Should().Be(CorridorId);
    }

    [Fact]
    public void FirstCopy_Places_LikeNormalTile()
    {
        var module = BuildModule(corridorCopies: 2);
        var state = NewState(module);
        // Start tile at (0,0); drop everything else so only CorridorId is at top.
        state.TileDeck.Clear();
        state.TileDeck.Add(CorridorId);
        state.TileDeck.Add(CorridorId);

        TileDeckService.Place(state, module, CorridorId, new Position(1, 0));

        state.TileMap.ContainsKey((1, 0)).Should().BeTrue();
        state.TileMap[(1, 0)].TileId.Should().Be(CorridorId);
    }

    [Fact]
    public void SecondCopy_MustBeAdjacentToExistingCopy()
    {
        var module = BuildModule(corridorCopies: 2);
        var state = NewState(module);
        state.TileDeck.Clear();
        state.TileDeck.Add(CorridorId);
        state.TileDeck.Add(CorridorId);

        // Place first copy at (1,0) — adjacent to start (0,0).
        TileDeckService.Place(state, module, CorridorId, new Position(1, 0));

        // Valid cells for the 2nd copy should only be those adjacent to (1,0).
        var candidates = TileDeckService.GetValidPlacementCells(state, module, CorridorId)
            .Select(p => (p.X, p.Y)).ToHashSet();
        candidates.Should().Contain((2, 0));
        candidates.Should().Contain((1, 1));
        candidates.Should().Contain((1, -1));
        candidates.Should().NotContain((-1, 0));
        candidates.Should().NotContain((0, 1));
    }

    [Fact]
    public void SecondCopy_Throws_WhenPlacedFarFromFirstCopy()
    {
        var module = BuildModule(corridorCopies: 2);
        var state = NewState(module);
        state.TileDeck.Clear();
        state.TileDeck.Add(CorridorId);
        state.TileDeck.Add(CorridorId);

        TileDeckService.Place(state, module, CorridorId, new Position(1, 0));

        // (0,1) is adjacent to start but NOT to the corridor copy at (1,0) — must throw.
        Action placeFar = () => TileDeckService.Place(state, module, CorridorId, new Position(0, 1));
        placeFar.Should().Throw<InvalidOperationException>()
            .WithMessage("*has existing copies*");
    }

    [Fact]
    public void SecondCopy_CanBePlacedAdjacent_FormingHorizontalCorridor()
    {
        var module = BuildModule(corridorCopies: 2);
        var state = NewState(module);
        state.TileDeck.Clear();
        state.TileDeck.Add(CorridorId);
        state.TileDeck.Add(CorridorId);

        TileDeckService.Place(state, module, CorridorId, new Position(1, 0));
        TileDeckService.Place(state, module, CorridorId, new Position(2, 0));

        state.TileMap[(1, 0)].TileId.Should().Be(CorridorId);
        state.TileMap[(2, 0)].TileId.Should().Be(CorridorId);
        state.TileDeck.Should().BeEmpty();
    }

    [Fact]
    public void HasPlacedCopy_DetectsExistingTile()
    {
        var module = BuildModule();
        var state = NewState(module);
        TileDeckService.HasPlacedCopy(state, StartId).Should().BeTrue();
        TileDeckService.HasPlacedCopy(state, CorridorId).Should().BeFalse();
    }

    // ─── Corridor direction lock (no L-shapes after 2 segments) ──────────────

    [Fact]
    public void ThirdSegment_MustExtendExistingDirection_Horizontal()
    {
        var module = BuildModule(corridorCopies: 3);
        var state = NewState(module);
        state.TileDeck.Clear();
        for (int i = 0; i < 3; i++) state.TileDeck.Add(CorridorId);

        // First two segments form a horizontal run at y=0.
        TileDeckService.Place(state, module, CorridorId, new Position(1, 0));
        TileDeckService.Place(state, module, CorridorId, new Position(2, 0));

        // Third segment valid cells: only (0,0 occupied by start → no) and (3,0).
        // Actually (0,0) is the starting tile, so only (3,0) remains.
        var valid = TileDeckService.GetValidPlacementCells(state, module, CorridorId)
            .Select(p => (p.X, p.Y)).ToHashSet();
        valid.Should().Contain((3, 0));
        valid.Should().NotContain((2, 1));   // would form an L
        valid.Should().NotContain((2, -1));  // would form an L
        valid.Should().NotContain((1, 1));   // would form an L
        valid.Should().NotContain((1, -1));  // would form an L
    }

    [Fact]
    public void ThirdSegment_PlacingOffAxis_Throws()
    {
        var module = BuildModule(corridorCopies: 3);
        var state = NewState(module);
        state.TileDeck.Clear();
        for (int i = 0; i < 3; i++) state.TileDeck.Add(CorridorId);

        TileDeckService.Place(state, module, CorridorId, new Position(1, 0));
        TileDeckService.Place(state, module, CorridorId, new Position(2, 0));

        Action lShape = () => TileDeckService.Place(state, module, CorridorId, new Position(2, 1));
        lShape.Should().Throw<InvalidOperationException>()
            .WithMessage("*colinear*");
    }

    [Fact]
    public void IsAlongCorridorDirection_AllowsBothEndpoints_WhenFewerThanTwoPlaced()
    {
        // <2 placed → no direction yet; any cell should pass the direction check.
        var placed = new List<Position> { new Position(1, 0) };
        TileDeckService.IsAlongCorridorDirection(placed, new Position(2, 0)).Should().BeTrue();
        TileDeckService.IsAlongCorridorDirection(placed, new Position(1, 1)).Should().BeTrue();
    }

    // ─── First-segment single-neighbor rule ──────────────────────────────────

    [Fact]
    public void FirstSegment_RequiresExactlyOnePlacedNeighbor()
    {
        var module = BuildModule(corridorCopies: 2);
        var state = NewState(module);
        // Surround (1,0) with placed tiles so it has 2 neighbors when attempting corridor placement.
        state.TileMap[(1, 1)] = new PlacedTile { TileId = FillerId };
        // Now (1,0) is adjacent to both the starting tile at (0,0) and the filler at (1,1) = 2 neighbors.
        // Corridor's first segment should NOT be placeable at (1,0).
        state.TileDeck.Clear();
        state.TileDeck.Add(CorridorId);
        state.TileDeck.Add(CorridorId);

        var valid = TileDeckService.GetValidPlacementCells(state, module, CorridorId)
            .Select(p => (p.X, p.Y)).ToHashSet();
        valid.Should().NotContain((1, 0)); // 2 neighbors → rejected
    }

    [Fact]
    public void FirstSegment_PlaceWithTwoNeighbors_Throws()
    {
        var module = BuildModule(corridorCopies: 2);
        var state = NewState(module);
        state.TileMap[(1, 1)] = new PlacedTile { TileId = FillerId };
        state.TileDeck.Clear();
        state.TileDeck.Add(CorridorId);
        state.TileDeck.Add(CorridorId);

        Action act = () => TileDeckService.Place(state, module, CorridorId, new Position(1, 0));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must be placed where it has exactly one placed neighbor*");
    }

    [Fact]
    public void FirstSegment_SingleNeighborRule_DoesNotApplyToCopiesEquals1()
    {
        // Regular (non-corridor) tiles may be placed next to multiple neighbors.
        var module = BuildModule(corridorCopies: 1); // not a corridor tile
        var state = NewState(module);
        state.TileMap[(1, 1)] = new PlacedTile { TileId = FillerId };
        state.TileDeck.Clear();
        state.TileDeck.Add(CorridorId);

        var valid = TileDeckService.GetValidPlacementCells(state, module, CorridorId)
            .Select(p => (p.X, p.Y)).ToHashSet();
        valid.Should().Contain((1, 0)); // allowed for single-copy tiles
    }

    [Fact]
    public void CountAdjacentPlaced_ReturnsNumberOfOrthogonalNeighbors()
    {
        var module = BuildModule();
        var state = NewState(module);
        state.TileMap[(1, 0)] = new PlacedTile { TileId = FillerId };
        state.TileMap[(0, 1)] = new PlacedTile { TileId = FillerId };

        TileDeckService.CountAdjacentPlaced(state, new Position(0, 0)).Should().Be(2); // (1,0) + (0,1)
        TileDeckService.CountAdjacentPlaced(state, new Position(1, 1)).Should().Be(2); // (1,0) + (0,1)
        TileDeckService.CountAdjacentPlaced(state, new Position(2, 0)).Should().Be(1); // (1,0) only
        TileDeckService.CountAdjacentPlaced(state, new Position(5, 5)).Should().Be(0);
    }

    // ─── Region-bridge (≥2 tags) single-neighbor rule ─────────────────────────

    private const string BridgeId = "bridge";
    private const string FillerOutdoorId = "filler-outdoor";

    /// <summary>
    /// Build a module with:
    ///   - start tile (neutral, no tags)
    ///   - a filler tile tagged "outdoor" so the map has outdoor territory
    ///   - a bridge tile tagged "outdoor"+"indoor" (region bridge; Copies=1)
    /// </summary>
    private static Module BuildBridgeModule()
    {
        var manifest = new Manifest("test", "Test", "1.0", "", 1, "", "");
        var prologue = new Prologue(
            "Test", 1, "",
            new List<WinCondition>(),
            new List<LoseCondition>(),
            new DifficultyCurve(
                new DifficultyRange(new[] { 8, 10 }),
                new DifficultyRange(new[] { 10, 12 }),
                new ClimaxCurve(14, 1, 18)),
            StartId,
            Array.Empty<string>(),
            0,
            "contributions * 2 + equipmentSlotsFilled");
        var tiles = new Dictionary<string, Tile>
        {
            [StartId] = new Tile(StartId, "Start", Terrain.Town, false,
                Array.Empty<ActionType>(), Array.Empty<TileResource>(), Array.Empty<EffectBase>()),
            [FillerOutdoorId] = new Tile(FillerOutdoorId, "Outdoor Filler", Terrain.Wilderness, false,
                Array.Empty<ActionType>(), Array.Empty<TileResource>(), Array.Empty<EffectBase>())
                { Tags = new[] { "outdoor" } },
            [BridgeId] = new Tile(BridgeId, "Foyer Bridge", Terrain.Dungeon, false,
                Array.Empty<ActionType>(), Array.Empty<TileResource>(), Array.Empty<EffectBase>())
                { Tags = new[] { "outdoor", "indoor" } },
        };
        var characters = new Dictionary<string, Character>
        {
            ["hero"] = new Character("hero", "Hero",
                new StatBlock(1, 1, 1, 1), 10, "", Array.Empty<string>()),
        };
        return new Module(
            manifest, prologue, characters,
            new Dictionary<string, NpcCompanion>(),
            tiles,
            new Dictionary<string, EventCard>(),
            new Dictionary<string, ActionCard>(),
            new Dictionary<string, Equipment>(),
            new Dictionary<string, Ending>(),
            new Dictionary<string, BattleCard>());
    }

    [Fact]
    public void BridgeTile_RequiresExactlyOnePlacedNeighbor()
    {
        var module = BuildBridgeModule();
        var state = NewState(module);
        // Start tile is at (0,0) untagged (neutral bridge). Place two outdoor filler tiles
        // so cell (1,0) has two placed neighbors: start(0,0) + filler(1,1).
        state.TileMap[(1, 1)] = new PlacedTile { TileId = FillerOutdoorId };
        state.TileDeck.Clear();
        state.TileDeck.Add(BridgeId);

        var valid = TileDeckService.GetValidPlacementCells(state, module, BridgeId)
            .Select(p => (p.X, p.Y)).ToHashSet();
        valid.Should().NotContain((1, 0)); // 2 neighbors → reject (no room for indoor tiles)
    }

    [Fact]
    public void BridgeTile_PlacingWithTwoNeighbors_Throws()
    {
        var module = BuildBridgeModule();
        var state = NewState(module);
        state.TileMap[(1, 1)] = new PlacedTile { TileId = FillerOutdoorId };
        state.TileDeck.Clear();
        state.TileDeck.Add(BridgeId);

        Action act = () => TileDeckService.Place(state, module, BridgeId, new Position(1, 0));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*region-bridge*must be placed where it has exactly one placed neighbor*");
    }

    [Fact]
    public void BridgeTile_WithSingleNeighbor_IsAccepted()
    {
        var module = BuildBridgeModule();
        var state = NewState(module);
        // Place filler at (1,0) so (2,0) has exactly one neighbor = the filler.
        state.TileMap[(1, 0)] = new PlacedTile { TileId = FillerOutdoorId };
        state.TileDeck.Clear();
        state.TileDeck.Add(BridgeId);

        // (2,0) has only (1,0) as neighbor → should be accepted (and it shares "outdoor" tag).
        Action act = () => TileDeckService.Place(state, module, BridgeId, new Position(2, 0));
        act.Should().NotThrow();
        state.TileMap[(2, 0)].TileId.Should().Be(BridgeId);
    }

    [Fact]
    public void SingleTagTile_IgnoresBridgeRule_AllowsMultipleNeighbors()
    {
        // A regular single-tag (non-bridge, copies=1) tile should not be restricted
        // by the single-neighbor rule — only bridges and multi-copy corridors are.
        var module = BuildBridgeModule();
        var state = NewState(module);
        state.TileMap[(1, 1)] = new PlacedTile { TileId = FillerOutdoorId };
        state.TileDeck.Clear();
        state.TileDeck.Add(FillerOutdoorId);

        // (1,0) has 2 neighbors but FillerOutdoorId has only 1 tag → allowed.
        var valid = TileDeckService.GetValidPlacementCells(state, module, FillerOutdoorId)
            .Select(p => (p.X, p.Y)).ToHashSet();
        valid.Should().Contain((1, 0));
    }
}
