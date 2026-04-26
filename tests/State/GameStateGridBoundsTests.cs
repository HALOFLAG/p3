// Phase 2 任務 11 Stage 0 — GameState 9×9 bounded grid + custom start position 測試。
// 驗證：
// - GridSize=null 時 IsInBounds 永遠 true（M-series 既有 unbounded 行為）
// - GridSize=9 時 IsInBounds 限制 0..8
// - CreateNew 帶 startPosition=(4,4) 時起始 tile 放在 (4,4) 與玩家位置同
// - CreateNew 不帶 startPosition 時維持原 (0,0) 行為（M-series 相容）
// - CreateNew 起始位置在 gridSize 外時拋例外
// - TileDeckService.Place 對出界格拋例外
// - GetValidPlacementCells 過濾出界鄰居
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.State;

public class GameStateGridBoundsTests
{
    private static (Module module, GameState state) NewBounded((int x, int y) startPos, int gridSize = 9)
    {
        var module = ModuleFactory.Load();
        var chars = module.Characters.Keys.Take(1).ToList();
        var comps = module.NpcCompanions.Keys.Take(1).ToList();
        var state = GameState.CreateNew(
            module, chars, comps, seed: 1234,
            gridSize: gridSize,
            startPosition: new Position(startPos.x, startPos.y));
        return (module, state);
    }

    // === GridSize / IsInBounds ===

    [Fact]
    public void IsInBounds_GridSizeNull_AlwaysTrue()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module); // 不帶 gridSize 參數 → null
        state.GridSize.Should().BeNull();
        state.IsInBounds(0, 0).Should().BeTrue();
        state.IsInBounds(-100, -100).Should().BeTrue();
        state.IsInBounds(9999, 9999).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(8, 8, true)]
    [InlineData(4, 4, true)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(9, 0, false)]
    [InlineData(0, 9, false)]
    [InlineData(9, 9, false)]
    public void IsInBounds_GridSize9_ChecksRange(int x, int y, bool expected)
    {
        var (_, state) = NewBounded((4, 4));
        state.GridSize.Should().Be(9);
        state.IsInBounds(x, y).Should().Be(expected);
    }

    // === CreateNew startPosition ===

    [Fact]
    public void CreateNew_DefaultStartPosition_PlacesAt0_0_PreservesMSeriesBehavior()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module); // 不帶 startPosition

        state.TileMap.Keys.Should().Contain((0, 0));
        state.Players[0].Position.Should().Be(new Position(0, 0));
    }

    [Fact]
    public void CreateNew_StartPosition4_4_PlacesStartingTileAt4_4()
    {
        var (module, state) = NewBounded((4, 4));

        state.TileMap.Keys.Should().Contain((4, 4));
        state.TileMap.Keys.Should().NotContain((0, 0));
        state.TileMap[(4, 4)].TileId.Should().Be(module.Prologue.StartingTileId);
        state.Players[0].Position.Should().Be(new Position(4, 4));
    }

    [Fact]
    public void CreateNew_StartPositionOutOfBounds_Throws()
    {
        var module = ModuleFactory.Load();
        var act = () => GameState.CreateNew(
            module, module.Characters.Keys.Take(1).ToList(),
            module.NpcCompanions.Keys.Take(1).ToList(), seed: 1,
            gridSize: 9, startPosition: new Position(10, 10));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*startPosition*outside*bounds*");
    }

    // === TileDeckService.Place bounds ===

    [Fact]
    public void Place_OutOfBoundsCell_Throws()
    {
        var (module, state) = NewBounded((4, 4));

        // 找一個能合法放的 tile（top of deck）
        var topTileId = state.TileDeck[0];
        var act = () => TileDeckService.Place(state, module, topTileId, new Position(-1, 4));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*outside grid bounds*");
    }

    // === GetValidPlacementCells filters out-of-bounds ===

    [Fact]
    public void GetValidPlacementCells_AtCorner_FiltersOutOfBoundsNeighbors()
    {
        // 用一個 corner case：玩家起點放 (0,0) 並設 gridSize=9
        var module = ModuleFactory.Load();
        var state = GameState.CreateNew(
            module, module.Characters.Keys.Take(1).ToList(),
            module.NpcCompanions.Keys.Take(1).ToList(), seed: 1,
            gridSize: 9, startPosition: new Position(0, 0));

        var cells = TileDeckService.GetValidPlacementCells(state);
        // 起點 (0,0) 的 4 個鄰居：(1,0) (-1,0) (0,1) (0,-1)
        // GridSize=9 過濾後只剩 (1,0) 與 (0,1)
        cells.Should().HaveCount(2);
        cells.Should().Contain(p => p.X == 1 && p.Y == 0);
        cells.Should().Contain(p => p.X == 0 && p.Y == 1);
        cells.Should().NotContain(p => p.X == -1);
        cells.Should().NotContain(p => p.Y == -1);
    }

    [Fact]
    public void GetValidPlacementCells_Unbounded_IncludesNegativeNeighbors()
    {
        // 對照組：未設 gridSize 應該仍包含負座標鄰居（M-series 行為）
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        // ModuleFactory 起點 (0,0) 不帶 gridSize → null

        var cells = TileDeckService.GetValidPlacementCells(state);
        cells.Should().Contain(p => p.X == -1 && p.Y == 0);
        cells.Should().Contain(p => p.X == 0 && p.Y == -1);
    }
}
