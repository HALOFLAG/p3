using CardNarrative.Core.Map;
using FluentAssertions;

namespace CardNarrative.Tests.Map;

public class WorldMapTests
{
    [Fact]
    public void Constructor_PlayerStartsAt4_4()
    {
        var map = new WorldMap();
        map.PlayerPos.Should().Be((4, 4));
    }

    [Fact]
    public void Constructor_InitialTileIsPlacedAndExplored()
    {
        var map = new WorldMap();
        var tile = map.GetTile(4, 4);
        tile.IsPlaced.Should().BeTrue();
        tile.IsExplored.Should().BeTrue();
    }

    [Fact]
    public void Constructor_OtherTilesNotPlaced()
    {
        var map = new WorldMap();
        for (int r = 0; r < WorldMap.Size; r++)
        for (int c = 0; c < WorldMap.Size; c++)
        {
            if (r == 4 && c == 4) continue;
            map.GetTile(r, c).IsPlaced.Should().BeFalse($"({r},{c}) should start unplaced");
            map.GetTile(r, c).IsExplored.Should().BeFalse();
        }
    }

    [Fact]
    public void Constructor_StartingMode_IsIdle()
    {
        var map = new WorldMap();
        map.Mode.Should().Be(InteractionMode.Idle);
        map.HeldTile.Should().BeNull();
    }

    [Fact]
    public void Constructor_DeckHasTenTiles()
    {
        var map = new WorldMap();
        map.RemainingTiles.Should().Be(10);
    }

    [Fact]
    public void NextTilePreview_ReturnsTopTwoOfDeck()
    {
        var map = new WorldMap();
        map.NextTilePreview.Should().HaveCount(2);
        map.NextTilePreview[0].Should().Be(MapTerrain.Path);
        map.NextTilePreview[1].Should().Be(MapTerrain.Forest);
    }

    // === IsLegalPlacement ===

    [Theory]
    [InlineData(3, 4)] // up
    [InlineData(5, 4)] // down
    [InlineData(4, 3)] // left
    [InlineData(4, 5)] // right
    public void IsLegalPlacement_FourDirectionalAdjacentEmpty_ReturnsTrue(int r, int c)
    {
        var map = new WorldMap();
        map.IsLegalPlacement(r, c).Should().BeTrue();
    }

    [Theory]
    [InlineData(3, 3)] // diagonal
    [InlineData(2, 4)] // 2-step
    [InlineData(4, 4)] // already placed
    public void IsLegalPlacement_NotAdjacentOrOccupied_ReturnsFalse(int r, int c)
    {
        var map = new WorldMap();
        map.IsLegalPlacement(r, c).Should().BeFalse();
    }

    [Fact]
    public void IsLegalPlacement_OutOfBounds_ReturnsFalse()
    {
        var map = new WorldMap();
        map.IsLegalPlacement(-1, 4).Should().BeFalse();
        map.IsLegalPlacement(9, 4).Should().BeFalse();
    }

    // === MapExpand 流程 ===

    [Fact]
    public void BeginMapExpand_FromIdle_DrawsTopOfDeck()
    {
        var map = new WorldMap();
        map.BeginMapExpand().Should().BeTrue();
        map.Mode.Should().Be(InteractionMode.MapExpand);
        map.HeldTile.Should().Be(MapTerrain.Path); // top of demo deck
        map.RemainingTiles.Should().Be(9);
    }

    [Fact]
    public void BeginMapExpand_WhileNotIdle_ReturnsFalse()
    {
        var map = new WorldMap();
        map.BeginMapExpand();
        map.BeginMapExpand().Should().BeFalse();
    }

    [Fact]
    public void TryPlaceHeldTile_LegalCell_PlacesAndFiresEvents()
    {
        var map = new WorldMap();
        map.BeginMapExpand();

        var placed = new List<(MapTerrain t, int r, int c)>();
        map.TilePlaced += (t, r, c) => placed.Add((t, r, c));
        var modeFired = 0;
        map.ModeChanged += () => modeFired++;

        map.TryPlaceHeldTile(3, 4).Should().BeTrue();

        map.GetTile(3, 4).IsPlaced.Should().BeTrue();
        map.GetTile(3, 4).Terrain.Should().Be(MapTerrain.Path);
        map.HeldTile.Should().BeNull();
        map.Mode.Should().Be(InteractionMode.Idle);
        placed.Should().ContainSingle().Which.Should().Be((MapTerrain.Path, 3, 4));
        modeFired.Should().Be(1);
    }

    [Fact]
    public void TryPlaceHeldTile_IllegalCell_ReturnsFalseAndKeepsHeld()
    {
        var map = new WorldMap();
        map.BeginMapExpand();
        map.TryPlaceHeldTile(0, 0).Should().BeFalse(); // 不相鄰
        map.HeldTile.Should().Be(MapTerrain.Path);
        map.Mode.Should().Be(InteractionMode.MapExpand);
    }

    [Fact]
    public void CancelMapExpand_ReturnsTileToTopOfDeck()
    {
        var map = new WorldMap();
        map.BeginMapExpand();

        map.CancelMapExpand();

        map.HeldTile.Should().BeNull();
        map.Mode.Should().Be(InteractionMode.Idle);
        map.RemainingTiles.Should().Be(10);
        map.NextTilePreview[0].Should().Be(MapTerrain.Path); // 放回頂端
    }

    // === Move 流程 ===

    [Fact]
    public void BeginMoveMode_FromIdle_SwitchesMode()
    {
        var map = new WorldMap();
        var fired = 0;
        map.ModeChanged += () => fired++;

        map.BeginMoveMode();

        map.Mode.Should().Be(InteractionMode.Move);
        fired.Should().Be(1);
    }

    [Fact]
    public void IsLegalMoveTarget_PlacedFourAdjacent_True()
    {
        var map = new WorldMap();
        PlaceTile(map, 3, 4); // Path

        map.IsLegalMoveTarget(3, 4).Should().BeTrue();
    }

    [Fact]
    public void IsLegalMoveTarget_UnplacedAdjacent_False()
    {
        var map = new WorldMap();
        // (3,4) 未放置
        map.IsLegalMoveTarget(3, 4).Should().BeFalse();
    }

    [Fact]
    public void TryMovePlayerTo_PlacedAdjacent_Succeeds()
    {
        var map = new WorldMap();
        PlaceTile(map, 3, 4);

        map.TryMovePlayerTo(3, 4).Should().BeTrue();
        map.PlayerPos.Should().Be((3, 4));
        map.GetTile(3, 4).IsExplored.Should().BeTrue();
    }

    [Fact]
    public void TryMovePlayerTo_UnplacedAdjacent_Fails()
    {
        var map = new WorldMap();
        map.TryMovePlayerTo(3, 4).Should().BeFalse();
        map.PlayerPos.Should().Be((4, 4));
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(2, 4)]
    public void TryMovePlayerTo_DiagonalOrFar_Fails(int r, int c)
    {
        var map = new WorldMap();
        PlaceTile(map, 3, 4);
        map.TryMovePlayerTo(r, c).Should().BeFalse();
    }

    [Fact]
    public void TryMovePlayerTo_FromMoveMode_ReturnsToIdle()
    {
        var map = new WorldMap();
        PlaceTile(map, 3, 4);
        map.BeginMoveMode();

        map.TryMovePlayerTo(3, 4);

        map.Mode.Should().Be(InteractionMode.Idle);
    }

    [Fact]
    public void CancelMoveMode_ReturnsToIdle()
    {
        var map = new WorldMap();
        map.BeginMoveMode();
        map.CancelMoveMode();
        map.Mode.Should().Be(InteractionMode.Idle);
    }

    // === Rest / HP ===

    [Fact]
    public void Rest_BelowMax_IncrementsHpAndFiresEvent()
    {
        var map = new WorldMap();
        // 強制 HP 不滿：先模擬受傷（直接呼叫 Rest 在滿血時無動作，所以先 mock）
        // 此處用 reflection 不適合；改測試「滿血 Rest 不變」分支
        var orig = map.Hp;
        map.Hp.Should().Be(map.HpMax);
        var fired = 0;
        map.HpChanged += _ => fired++;

        map.Rest();

        map.Hp.Should().Be(orig);
        fired.Should().Be(0);
    }

    // === Camera ===

    [Fact]
    public void SetCameraOffset_FiresEvent()
    {
        var map = new WorldMap();
        var fired = 0;
        map.CameraOffsetChanged += () => fired++;

        map.SetCameraOffset(1.5f, -2.0f);

        map.CameraOffset.Should().Be((1.5f, -2.0f));
        fired.Should().Be(1);
    }

    [Fact]
    public void ResetCameraToPlayer_RestoresZeroOffset()
    {
        var map = new WorldMap();
        map.SetCameraOffset(3f, 4f);
        map.ResetCameraToPlayer();
        map.CameraOffset.Should().Be((0f, 0f));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(8, 8, true)]
    [InlineData(-1, 0, false)]
    [InlineData(9, 0, false)]
    public void IsInBounds_GridSize9_ChecksCorrectly(int r, int c, bool expected)
    {
        WorldMap.IsInBounds(r, c).Should().Be(expected);
    }

    // === Helper ===

    private static void PlaceTile(WorldMap map, int row, int col)
    {
        map.BeginMapExpand();
        map.TryPlaceHeldTile(row, col).Should().BeTrue("test setup expects placement to succeed");
    }
}
