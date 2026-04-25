// B2 · TransformTile / TransformTilesByTag effect 單元測試。
// 驗證：
// - 單格轉變更新 TileId 並重置 Level / 採集 / interaction usage
// - X/Y 省略時使用 CurrentPlayer.Position
// - PreservePlayerPosition=false 時玩家被推到相鄰 tile
// - TransformTilesByTag AND 語義（需包含所有指定 tag）
// - Module 中找不到 newTileId 時 no-op
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class TransformTileEffectTests
{
    [Fact]
    public void TransformTile_WithExplicitCoords_UpdatesTileIdAndResetsState()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        // 放置一格 warehouse 並「熟悉」之；然後轉變為 town-square。
        state.TileMap[(1, 0)] = new PlacedTile
        {
            TileId = "warehouse",
            Level = ExplorationLevel.Familiar,
            ProgressGainedThisBigRound = 1,
            ActionCardProgressUsedThisVisit = true,
            LastVisitBigRound = 3
        };
        state.TileMap[(1, 0)].ResourcesCollectedByPlayer[0] = new HashSet<string> { "clue" };
        state.TileMap[(1, 0)].InteractionsUsedOncePerGame.Add("talk-to-guard");

        var handler = new EffectHandler();
        handler.Apply(new TransformTileEffect("town-square", X: 1, Y: 0), state, module);

        var placed = state.TileMap[(1, 0)];
        placed.TileId.Should().Be("town-square");
        placed.Level.Should().Be(ExplorationLevel.Unknown);
        placed.ProgressGainedThisBigRound.Should().Be(0);
        placed.ActionCardProgressUsedThisVisit.Should().BeFalse();
        placed.LastVisitBigRound.Should().Be(0);
        placed.ResourcesCollectedByPlayer.Should().BeEmpty();
        placed.InteractionsUsedOncePerGame.Should().BeEmpty();
    }

    [Fact]
    public void TransformTile_WithoutCoords_UsesCurrentPlayerPosition()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(2, 2)] = new PlacedTile { TileId = "warehouse", Level = ExplorationLevel.Unfamiliar };
        state.CurrentPlayer.Position = new Position(2, 2);

        var handler = new EffectHandler();
        handler.Apply(new TransformTileEffect("town-square"), state, module);

        state.TileMap[(2, 2)].TileId.Should().Be("town-square");
    }

    [Fact]
    public void TransformTile_PreservePlayerPositionTrue_KeepsPlayerOnTile()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(0, 0)] = new PlacedTile { TileId = "warehouse", Level = ExplorationLevel.Unfamiliar };
        state.CurrentPlayer.Position = new Position(0, 0);

        new EffectHandler().Apply(
            new TransformTileEffect("town-square", X: 0, Y: 0, PreservePlayerPosition: true),
            state, module);

        state.CurrentPlayer.Position.Should().Be(new Position(0, 0));
    }

    [Fact]
    public void TransformTile_PreservePlayerPositionFalse_PushesPlayerToNeighbor()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        // 兩格相鄰：(0,0) 被轉變；(1,0) 作為推擠目標。
        state.TileMap[(0, 0)] = new PlacedTile { TileId = "warehouse", Level = ExplorationLevel.Unfamiliar };
        state.TileMap[(1, 0)] = new PlacedTile { TileId = "town-square", Level = ExplorationLevel.Unfamiliar };
        state.CurrentPlayer.Position = new Position(0, 0);

        new EffectHandler().Apply(
            new TransformTileEffect("town-square", X: 0, Y: 0, PreservePlayerPosition: false),
            state, module);

        state.CurrentPlayer.Position.Should().Be(new Position(1, 0));
    }

    [Fact]
    public void TransformTile_UnknownNewTileId_NoOp()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(0, 0)] = new PlacedTile { TileId = "warehouse", Level = ExplorationLevel.Familiar };

        new EffectHandler().Apply(new TransformTileEffect("does-not-exist", X: 0, Y: 0), state, module);

        state.TileMap[(0, 0)].TileId.Should().Be("warehouse");
        state.TileMap[(0, 0)].Level.Should().Be(ExplorationLevel.Familiar);
    }

    [Fact]
    public void TransformTilesByTag_MatchesTilesWithAllTags()
    {
        var module = ModuleFactory.Load();
        // 把 warehouse 注入 tag["derelict","indoor"]；town-square 注入 tag["derelict"] 但缺 indoor；
        // wilderness-path 沒有 derelict tag。只有 warehouse 符合 AND 條件。
        var tiles = (Dictionary<string, Tile>)module.Tiles;
        tiles["warehouse"]       = tiles["warehouse"]       with { Tags = new[] { "derelict", "indoor" } };
        tiles["town-square"]     = tiles["town-square"]     with { Tags = new[] { "derelict" } };
        tiles["wilderness-path"] = tiles["wilderness-path"] with { Tags = new[] { "indoor" } };

        var state = ModuleFactory.NewState(module);
        state.TileMap[(0, 0)] = new PlacedTile { TileId = "warehouse",       Level = ExplorationLevel.Familiar };
        state.TileMap[(1, 0)] = new PlacedTile { TileId = "town-square",     Level = ExplorationLevel.Familiar };
        state.TileMap[(2, 0)] = new PlacedTile { TileId = "wilderness-path", Level = ExplorationLevel.Familiar };

        new EffectHandler().Apply(
            new TransformTilesByTagEffect(new[] { "derelict", "indoor" }, "town-square"),
            state, module);

        state.TileMap[(0, 0)].TileId.Should().Be("town-square");           // matched → transformed
        state.TileMap[(0, 0)].Level.Should().Be(ExplorationLevel.Unknown); // reset
        state.TileMap[(1, 0)].TileId.Should().Be("town-square");           // already this id, but tags match → still "transforms" (re-resets)
        state.TileMap[(2, 0)].TileId.Should().Be("wilderness-path");       // did not match (missing derelict)
        state.TileMap[(2, 0)].Level.Should().Be(ExplorationLevel.Familiar);
    }

    [Fact]
    public void TransformTilesByTag_EmptyTags_NoOp()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(0, 0)] = new PlacedTile { TileId = "warehouse", Level = ExplorationLevel.Familiar };

        new EffectHandler().Apply(
            new TransformTilesByTagEffect(Array.Empty<string>(), "town-square"),
            state, module);

        state.TileMap[(0, 0)].TileId.Should().Be("warehouse");
    }
}
