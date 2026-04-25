using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Integration;

public class TileSystemEndToEndTests
{
    [Fact]
    public void FullBigRound_Move_Action_Clue_Expand_Reset()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, players: 1);
        // Pre-seed an adjacent tile at (1,0). With the new phase order
        // (Draw → Move → Action → EventCheck → MapExpand → TurnEnd),
        // tile placement occurs at end of turn, so Move in turn 1 needs an
        // already-adjacent tile.
        var target = new Position(1, 0);
        var seededTileId = state.TileDeck[0];
        state.TileDeck.RemoveAt(0);
        state.TileMap[(target.X, target.Y)] = new PlacedTile
        {
            TileId = seededTileId,
            Level = ExplorationLevel.Unknown
        };

        var loop = new TurnLoop(state, new SeededDiceService(1), module);

        // --- Turn 1: Draw → Action (Move+Action merged) → EventCheck → MapExpand → TurnEnd ---
        loop.Advance(); // Draw → Action
        loop.State.Phase.Should().Be(TurnPhase.Action);

        // Move into pre-seeded tile (Unknown → Unfamiliar) — in the merged Action phase
        loop.Move(target);
        loop.State.TileMap[(target.X, target.Y)].Level.Should().Be(ExplorationLevel.Unfamiliar);

        // If Move triggered an event, resolve it; phase stays Action either way
        if (loop.PendingEvent is not null) loop.ResolvePendingEvent();
        loop.State.Phase.Should().Be(TurnPhase.Action);

        // Play an allowed action card
        var player = loop.State.CurrentPlayer;
        var tile = module.Tiles[loop.State.TileMap[(target.X, target.Y)].TileId];
        var playable = player.Hand
            .Select(id => module.ActionCards[id])
            .FirstOrDefault(c => tile.AllowedActionTypes.Contains(c.Type));
        if (playable is not null)
        {
            loop.PlayCard(playable.Id, playable.Type switch
            {
                ActionType.Combat => Stat.Power,
                ActionType.Communication => Stat.Social,
                ActionType.Exploration => Stat.Skill,
                _ => Stat.Intellect
            }, 100);
            // Action-card path promoted the tile
            loop.State.TileMap[(target.X, target.Y)].Level.Should().Be(ExplorationLevel.Neutral);
            loop.State.CurrentPlayer.Discard.Should().Contain(playable.Id);
        }

        // Clue investment
        loop.State.Resources["clue_shard"] = 2;
        var before = loop.State.TileMap[(target.X, target.Y)].Level;
        loop.InvestCluesForProgress();
        ((int)loop.State.TileMap[(target.X, target.Y)].Level).Should().BeGreaterThan((int)before);
        loop.State.Resources["clue_shard"].Should().Be(0);

        // --- End big round ---
        loop.EndPlayerTurn();
        loop.State.CurrentBigRound.Should().Be(2);
        loop.State.TileMap[(target.X, target.Y)].ProgressGainedThisBigRound.Should().Be(0);
    }

    [Fact]
    public void MapExpand_BlocksAdvance_UntilUserActs()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, players: 1);
        var loop = new TurnLoop(state, new SeededDiceService(1), module);
        loop.Advance(); // Draw → Action (Move+Action merged)
        loop.Advance(); // Action → EventCheck → MapExpand (deck non-empty, stops here)
        loop.State.Phase.Should().Be(TurnPhase.MapExpand);

        // Repeat Advance doesn't progress because deck is non-empty
        loop.Advance();
        loop.Advance();
        loop.State.Phase.Should().Be(TurnPhase.MapExpand);

        loop.SkipPlacement();
        // TurnEnd auto-chains to next turn's Action
        loop.State.Phase.Should().Be(TurnPhase.Action);
    }
}
