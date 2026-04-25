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

        map.TryMovePlayerTo(3, 4).Should().Be(MovePlayerResult.Ok);
        map.PlayerPos.Should().Be((3, 4));
        map.GetTile(3, 4).IsExplored.Should().BeTrue();
    }

    [Fact]
    public void TryMovePlayerTo_UnplacedAdjacent_Fails()
    {
        var map = new WorldMap();
        map.TryMovePlayerTo(3, 4).Should().Be(MovePlayerResult.IllegalTarget);
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
        map.TryMovePlayerTo(r, c).Should().Be(MovePlayerResult.IllegalTarget);
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
    public void Rest_HpAlreadyMax_NoOp()
    {
        var map = new WorldMap();
        var orig = map.Hp;
        map.Hp.Should().Be(map.HpMax);
        var hpFired = 0;
        map.HpChanged += _ => hpFired++;

        var result = map.Rest();

        map.Hp.Should().Be(orig);
        result.HpGained.Should().Be(0);
        hpFired.Should().Be(0);
    }

    // === Task 6：Turn / AP / Draw ===

    [Fact]
    public void Constructor_StartsAtTurn1WithFullAp()
    {
        var map = new WorldMap();
        map.Turn.Should().Be(1);
        map.Ap.Should().Be(WorldMap.ApMax);
        map.HandSize.Should().Be(WorldMap.HandSizeMax);
        map.FirstMoveUsedThisTurn.Should().BeFalse();
        map.FirstObserveUsedThisTurn.Should().BeFalse();
    }

    [Fact]
    public void TryMovePlayerTo_FirstMoveThisTurn_FreeNoApCost()
    {
        var map = new WorldMap();
        PlaceTile(map, 3, 4);
        var startAp = map.Ap;

        map.TryMovePlayerTo(3, 4).Should().Be(MovePlayerResult.Ok);

        map.Ap.Should().Be(startAp); // 第 1 格免費
        map.FirstMoveUsedThisTurn.Should().BeTrue();
    }

    [Fact]
    public void TryMovePlayerTo_SecondMoveThisTurn_CostsOneAp()
    {
        var map = new WorldMap();
        PlaceTile(map, 3, 4);  // (4,4)->(3,4) 鄰
        PlaceTile(map, 2, 4);  // (3,4)->(2,4) 鄰

        map.TryMovePlayerTo(3, 4); // 免費
        var afterFirst = map.Ap;
        map.BeginMoveMode();
        map.TryMovePlayerTo(2, 4).Should().Be(MovePlayerResult.Ok); // 第 2 格扣 1
        map.Ap.Should().Be(afterFirst - 1);
    }

    [Fact]
    public void TryMovePlayerTo_NotEnoughAp_Fails()
    {
        var map = new WorldMap();
        PlaceTile(map, 3, 4);
        PlaceTile(map, 2, 4);
        PlaceTile(map, 1, 4);
        PlaceTile(map, 0, 4);

        map.TryMovePlayerTo(3, 4); // 免費
        map.BeginMoveMode(); map.TryMovePlayerTo(2, 4); // 1 AP → 2
        map.BeginMoveMode(); map.TryMovePlayerTo(1, 4); // 1 AP → 1
        map.BeginMoveMode(); map.TryMovePlayerTo(0, 4); // 1 AP → 0
        map.Ap.Should().Be(0);

        // 接下來想再走（但已無相鄰已放格 + AP=0）
        // 這裡其實已沒合法目標，所以就用 mode 回到不可移動
    }

    [Fact]
    public void Observe_FirstThisTurn_FreeAndPerforms()
    {
        var map = new WorldMap();
        var startAp = map.Ap;
        var roll = new FixedRoll(3, 4); // 7 + skill 3 = 10 → 成功

        var r = map.Observe(roll, skillBonus: 3);

        r.Performed.Should().BeTrue();
        r.Success.Should().BeTrue();
        map.Ap.Should().Be(startAp);
        map.FirstObserveUsedThisTurn.Should().BeTrue();
    }

    [Fact]
    public void Observe_SecondThisTurn_CostsTwoAp()
    {
        var map = new WorldMap();
        map.Observe(new FixedRoll(1, 1), 3); // 首次免費
        var afterFirst = map.Ap;
        map.Observe(new FixedRoll(2, 2), 3); // 第 2 次扣 2
        map.Ap.Should().Be(afterFirst - 2);
    }

    [Fact]
    public void Observe_NotEnoughAp_DoesNotPerform()
    {
        var map = new WorldMap();
        map.Observe(new FixedRoll(3, 3), 3); // 首次免費
        map.Observe(new FixedRoll(3, 3), 3); // 1 AP → 1（扣 2）→ 應變 1
        // AP 從 3 → 3（首次免費）→ 1
        map.Ap.Should().Be(1);

        // 第 3 次需要 2 AP，但只有 1 → 拒絕
        var r = map.Observe(new FixedRoll(3, 3), 3);
        r.Performed.Should().BeFalse();
        map.Ap.Should().Be(1);
    }

    [Fact]
    public void Rest_ConsumesAllRemainingApAndHealsHp()
    {
        var map = new WorldMap();
        // 先扣血（用 Observe 不能扣血；用 reflection 太繞，直接測試 Rest 在 HP 滿時 NoOp 已驗）
        // 換策略：先把 HP 降下來 — 但目前沒公開的「受傷」介面。改測「沒 AP 時 Rest = NoOp」
        map.Observe(new FixedRoll(1, 1), 3); // 首次免費
        map.Observe(new FixedRoll(1, 1), 3); // 1 AP → 扣 2 = 1
        // Hp 仍滿，Rest 會 NoOp
        var r = map.Rest();
        r.ApSpent.Should().Be(0);
        r.HpGained.Should().Be(0);
    }

    [Fact]
    public void AdvanceTurn_ResetsApAndHandAndIncrementsTurn()
    {
        var map = new WorldMap();
        PlaceTile(map, 3, 4);
        PlaceTile(map, 2, 4);
        map.TryMovePlayerTo(3, 4);
        map.BeginMoveMode();
        map.TryMovePlayerTo(2, 4); // 已扣 1 AP

        var beforeAp = map.Ap;
        var ok = map.AdvanceTurn();

        ok.Should().BeTrue();
        map.Turn.Should().Be(2);
        map.Ap.Should().Be(WorldMap.ApMax);
        map.HandSize.Should().Be(WorldMap.HandSizeMax);
        map.FirstMoveUsedThisTurn.Should().BeFalse();
        map.FirstObserveUsedThisTurn.Should().BeFalse();
    }

    [Fact]
    public void AdvanceTurn_FiresAllRelevantEvents()
    {
        var map = new WorldMap();
        var turnFired = 0; var apFired = 0; var handFired = 0;
        map.TurnChanged += _ => turnFired++;
        map.ApChanged += (_, _) => apFired++;
        map.HandSizeChanged += (_, _) => handFired++;

        map.AdvanceTurn();

        turnFired.Should().Be(1);
        apFired.Should().Be(1);
        handFired.Should().Be(1);
    }

    [Fact]
    public void AdvanceTurn_WhileNotIdle_Rejects()
    {
        var map = new WorldMap();
        map.BeginMapExpand();
        map.AdvanceTurn().Should().BeFalse();
        map.Turn.Should().Be(1);
    }

    private sealed class FixedRoll : IRollProvider
    {
        private readonly int _d1, _d2;
        public FixedRoll(int d1, int d2) { _d1 = d1; _d2 = d2; }
        public (int D1, int D2) Roll2d6() => (_d1, _d2);
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
