// Phase 3 移動 UX 改造 Stage 1 — MapPathFinding BFS 規格化測試（規格書 §1.5 / §3.1.4）。
// 驗證：
//   - 起點 = 目標 → 空 list
//   - 相鄰目標 → 單步路徑
//   - 多格路徑 → 正確最短路
//   - 目標未放置 → 空 list
//   - 不可達（被未放置格隔離）→ 空 list
//   - AP cost 計算（首格免費 / 已用首格）
using CardNarrative.Core.Cards;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;
using Xunit;

namespace CardNarrative.Tests.Services;

public class MapPathFindingTests
{
    private static GameState NewState9x9()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var module = ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;
        var heroId = module.Characters.Keys.First();
        return GameState.CreateNew(
            module, new[] { heroId },
            module.Prologue.StartingCompanionIds, seed: 1234,
            gridSize: 9, startPosition: new Position(4, 4));
    }

    /// <summary>輔助：在指定座標放置一個 tile（任意 TileId 即可，BFS 只查 ContainsKey）。</summary>
    private static void PlaceTile(GameState state, int x, int y, string tileId = "village-store")
    {
        state.TileMap[(x, y)] = new PlacedTile { TileId = tileId, Level = ExplorationLevel.Familiar };
    }

    [Fact]
    public void FindPath_StartEqualsGoal_ReturnsEmpty()
    {
        var state = NewState9x9();
        var pf = new MapPathFinding();

        var path = pf.FindPath(state, new Position(4, 4), new Position(4, 4));

        path.Should().BeEmpty();
    }

    [Fact]
    public void FindPath_AdjacentTile_ReturnsSingleStep()
    {
        // CreateNew 會在 (4,4) 放起始格；放 (4,5) 為目標
        var state = NewState9x9();
        PlaceTile(state, 4, 5);
        var pf = new MapPathFinding();

        var path = pf.FindPath(state, new Position(4, 4), new Position(4, 5));

        path.Should().HaveCount(1);
        path[0].Should().Be(new Position(4, 5));
    }

    [Fact]
    public void FindPath_ThreeStepPath_ReturnsThreeSteps()
    {
        // 從 (4,4) 走到 (4,7)，中間經 (4,5)、(4,6) — 三格全放
        var state = NewState9x9();
        PlaceTile(state, 4, 5);
        PlaceTile(state, 4, 6);
        PlaceTile(state, 4, 7);
        var pf = new MapPathFinding();

        var path = pf.FindPath(state, new Position(4, 4), new Position(4, 7));

        path.Should().HaveCount(3);
        path[0].Should().Be(new Position(4, 5));
        path[1].Should().Be(new Position(4, 6));
        path[2].Should().Be(new Position(4, 7));
    }

    [Fact]
    public void FindPath_GoalNotPlaced_ReturnsEmpty()
    {
        // (4,5) 未放置 → 不可走
        var state = NewState9x9();
        var pf = new MapPathFinding();

        var path = pf.FindPath(state, new Position(4, 4), new Position(4, 5));

        path.Should().BeEmpty();
    }

    [Fact]
    public void FindPath_DisconnectedGoal_ReturnsEmpty()
    {
        // 起點 (4,4) 已放；目標 (6,6) 已放，但中間 (4,5)/(4,6)/(5,6) 等全未放 → 不連通
        var state = NewState9x9();
        PlaceTile(state, 6, 6);
        var pf = new MapPathFinding();

        var path = pf.FindPath(state, new Position(4, 4), new Position(6, 6));

        path.Should().BeEmpty();
    }

    [Fact]
    public void FindPath_OutOfBounds_ReturnsEmpty()
    {
        // 9×9 邊界外（x=10）必然不在 TileMap 也不 InBounds
        var state = NewState9x9();
        var pf = new MapPathFinding();

        var path = pf.FindPath(state, new Position(4, 4), new Position(10, 4));

        path.Should().BeEmpty();
    }

    [Fact]
    public void CalculateApCost_FirstMoveAvailable_FirstStepIsFree()
    {
        // 路徑 3 格 + 首格免費 → 2 AP
        MapPathFinding.CalculateApCost(3, firstMoveAvailable: true).Should().Be(2);
        // 路徑 1 格 + 首格免費 → 0 AP
        MapPathFinding.CalculateApCost(1, firstMoveAvailable: true).Should().Be(0);
        // 路徑 0 格 → 0 AP
        MapPathFinding.CalculateApCost(0, firstMoveAvailable: true).Should().Be(0);
    }

    [Fact]
    public void CalculateApCost_FirstMoveAlreadyUsed_EveryStepCostsOne()
    {
        // 路徑 3 格 + 首格已用 → 3 AP
        MapPathFinding.CalculateApCost(3, firstMoveAvailable: false).Should().Be(3);
        // 路徑 1 格 + 首格已用 → 1 AP
        MapPathFinding.CalculateApCost(1, firstMoveAvailable: false).Should().Be(1);
    }
}
