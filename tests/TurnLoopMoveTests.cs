using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests;

public class TurnLoopMoveTests
{
    private static (TurnLoop loop, Module module) Setup()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.TileMap[(1, 0)] = new PlacedTile { TileId = "wilderness-path", Level = ExplorationLevel.Unfamiliar };
        state.TileMap[(2, 0)] = new PlacedTile { TileId = "warehouse", Level = ExplorationLevel.Unknown };
        state.Phase = TurnPhase.Action;
        state.CurrentPlayer.ActionPoints = 3;
        state.CurrentPlayer.MovesThisTurn = 0;
        return (new TurnLoop(state, new SeededDiceService(1), module, new EventScheduler()), module);
    }

    [Fact]
    public void Move_OneCell_Free()
    {
        var (loop, _) = Setup();
        int apBefore = loop.State.CurrentPlayer.ActionPoints;
        loop.Move(new Position(1, 0));
        loop.State.CurrentPlayer.Position.Should().Be(new Position(1, 0));
        loop.State.CurrentPlayer.ActionPoints.Should().Be(apBefore); // free
        loop.State.CurrentPlayer.MovesThisTurn.Should().Be(1);
    }

    [Fact]
    public void Move_SecondCell_SacrificesRemainingAp()
    {
        var (loop, _) = Setup();
        loop.Move(new Position(1, 0));
        // PendingEvent may fire for warehouse on 2nd move, but first target is wilderness-path.
        // Clear pending after first move (no trigger expected on wilderness-path: onEnter triggers only if scheduler matches tileEnter, which it doesn't for wilderness-path)
        loop.PendingEvent.Should().BeNull();

        loop.Move(new Position(2, 0));
        // Warehouse triggers tileEnter event; movement itself still succeeded
        loop.State.CurrentPlayer.Position.Should().Be(new Position(2, 0));
        loop.State.CurrentPlayer.ActionPoints.Should().Be(0);
        loop.State.CurrentPlayer.MovesThisTurn.Should().Be(2);
    }

    [Fact]
    public void Move_SecondMove_Throws_WhenNoAp()
    {
        var (loop, _) = Setup();
        loop.State.CurrentPlayer.ActionPoints = 0;
        loop.Move(new Position(1, 0)); // first free
        var act = () => loop.Move(new Position(0, 0));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Move_IntoFamiliarTile_Once_FreeAndMarksFlag()
    {
        var (loop, _) = Setup();
        loop.State.TileMap[(1, 0)].Level = ExplorationLevel.Familiar;

        var result = loop.Move(new Position(1, 0));

        result.UsedFamiliarFree.Should().BeTrue();
        loop.State.UsedFamiliarFreeMoveThisTurn.Should().BeTrue();
        loop.State.CurrentPlayer.MovesThisTurn.Should().Be(0); // didn't consume a move
    }

    [Fact]
    public void Move_RejectsNonAdjacent()
    {
        var (loop, _) = Setup();
        var act = () => loop.Move(new Position(5, 5));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Move_RejectsNoTileAtTarget()
    {
        var (loop, _) = Setup();
        var act = () => loop.Move(new Position(0, 1));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Move_TriggersOnEnterEvent_Warehouse()
    {
        var (loop, _) = Setup();
        loop.Move(new Position(1, 0));
        var result = loop.Move(new Position(2, 0));
        result.TriggeredOnEnter.Should().BeTrue();
        loop.PendingEvent.Should().NotBeNull();
    }

    [Fact]
    public void Move_PromotesUnknownToUnfamiliar_OnFirstEntry()
    {
        var (loop, _) = Setup();
        loop.State.TileMap[(1, 0)].Level = ExplorationLevel.Unknown;
        loop.Move(new Position(1, 0));
        loop.State.TileMap[(1, 0)].Level.Should().Be(ExplorationLevel.Unfamiliar);
    }

    [Fact]
    public void Move_ClearsPerVisitFlags()
    {
        var (loop, _) = Setup();
        var placed = loop.State.TileMap[(1, 0)];
        placed.ActionCardProgressUsedThisVisit = true;

        loop.Move(new Position(1, 0));

        placed.ActionCardProgressUsedThisVisit.Should().BeFalse();
        placed.LastVisitBigRound.Should().Be(loop.State.CurrentBigRound);
    }

    // §13-2 Plan A · 跨區域移動硬限制。由於 fixture tiles 皆無 tags，預設全相容；
    // 下列三支測試替換地塊定義以製造 tag 衝突 / 共享 / 中立三種情境。
    private static (TurnLoop loop, Module module) SetupWithTaggedTiles(
        IReadOnlyList<string> leftTags,
        IReadOnlyList<string> rightTags)
    {
        var baseModule = ModuleFactory.Load();
        var newTiles = new Dictionary<string, Tile>(baseModule.Tiles);
        var origin = newTiles[baseModule.Prologue.StartingTileId];
        newTiles[baseModule.Prologue.StartingTileId] = origin with { Tags = leftTags };
        newTiles["wilderness-path"] = newTiles["wilderness-path"] with { Tags = rightTags };
        var module = baseModule with { Tiles = newTiles };

        var state = ModuleFactory.NewState(module);
        state.TileMap[(1, 0)] = new PlacedTile { TileId = "wilderness-path", Level = ExplorationLevel.Unfamiliar };
        state.Phase = TurnPhase.Action;
        state.CurrentPlayer.ActionPoints = 3;
        state.CurrentPlayer.MovesThisTurn = 0;
        return (new TurnLoop(state, new SeededDiceService(1), module, new EventScheduler()), module);
    }

    [Fact]
    public void Move_BlockedWhenTagsDisjoint()
    {
        var (loop, _) = SetupWithTaggedTiles(new[] { "town" }, new[] { "forest" });
        var act = () => loop.Move(new Position(1, 0));
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*tags do not share*");
        loop.GetMoveTargets().Should().NotContain(new Position(1, 0));
    }

    [Fact]
    public void Move_AllowedWhenTagsShare()
    {
        var (loop, _) = SetupWithTaggedTiles(new[] { "outdoor", "town" }, new[] { "outdoor", "forest" });
        loop.Move(new Position(1, 0));
        loop.State.CurrentPlayer.Position.Should().Be(new Position(1, 0));
        loop.GetMoveTargets().Should().NotBeEmpty();
    }

    [Fact]
    public void Move_AllowedWhenEitherSideNeutral()
    {
        // 左側 tags 為空 → 中立橋接，應允許
        var (loop, _) = SetupWithTaggedTiles(Array.Empty<string>(), new[] { "forest" });
        loop.Move(new Position(1, 0));
        loop.State.CurrentPlayer.Position.Should().Be(new Position(1, 0));
    }

    [Fact]
    public void ActionPhase_AllowsInterleavedMoveAndPlayCard()
    {
        // Move and Action are merged; the player can move, play a card, and move again
        // within the same Action phase without any Advance() calls in between.
        var (loop, module) = Setup();
        var player = loop.State.CurrentPlayer;
        // Seed hand with a card whose type is allowed on the wilderness-path tile (Exploration/Thinking).
        var card = module.ActionCards.Values
            .First(c => c.Type == ActionType.Exploration && c.Cost <= 1);
        player.Hand.Clear();
        player.Hand.Add(card.Id);

        loop.State.Phase.Should().Be(TurnPhase.Action);

        // Move onto the wilderness-path tile
        loop.Move(new Position(1, 0));
        loop.PendingEvent.Should().BeNull();
        loop.State.Phase.Should().Be(TurnPhase.Action);

        // Play the card while still in Action phase — no Advance needed
        loop.PlayCard(card.Id, Stat.Skill, tn: 8);
        loop.State.Phase.Should().Be(TurnPhase.Action);
        loop.State.CurrentPlayer.Discard.Should().Contain(card.Id);
    }
}
