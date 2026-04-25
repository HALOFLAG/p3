using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests;

public class TurnLoopTests
{
    private (TurnLoop loop, Module module) Setup(int seed = 1234, int players = 2)
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.ValidModuleFolder);
        var module = (result as ModuleLoadResult.Success)!.Module;

        var characterIds = module.Characters.Keys.Take(players).ToList();
        var companionIds = module.NpcCompanions.Keys.Take(1).ToList();
        var state = GameState.CreateNew(module, characterIds, companionIds, seed);
        var dice = new SeededDiceService(seed);
        return (new TurnLoop(state, dice, module), module);
    }

    [Fact]
    public void Advance_StopsOnlyAtUserInputPhases()
    {
        var (loop, _) = Setup();
        loop.State.TileDeck.Clear(); // empty → MapExpand auto-skips
        loop.State.Phase.Should().Be(TurnPhase.Draw);

        loop.Advance(); loop.State.Phase.Should().Be(TurnPhase.Action); // Draw → Action (Move+Action merged)
        // Action → EventCheck → MapExpand(empty) → TurnEnd → next turn Draw → Action
        loop.Advance(); loop.State.Phase.Should().Be(TurnPhase.Action);
        loop.State.CurrentPlayerIndex.Should().Be(1);
    }

    [Fact]
    public void Draw_ResetsActionPoints_To_3()
    {
        var (loop, _) = Setup();
        loop.State.CurrentPlayer.ActionPoints = 0;
        loop.Advance();
        loop.State.CurrentPlayer.ActionPoints.Should().Be(TurnLoop.ActionPointsPerTurn);
    }

    [Fact]
    public void Draw_FillsHand_UpToLimit()
    {
        var (loop, module) = Setup();
        var deckSize = module.Characters[loop.State.Players[0].CharacterId].StartingDeck.Count;
        int expected = Math.Min(TurnLoop.HandSizeLimit, deckSize);
        loop.Advance();
        loop.State.Players[0].Hand.Should().HaveCount(expected);
    }

    [Fact]
    public void EndPlayerTurn_RotatesToNextPlayer()
    {
        var (loop, _) = Setup(players: 2);
        loop.State.CurrentPlayerIndex.Should().Be(0);

        loop.EndPlayerTurn();
        loop.State.CurrentPlayerIndex.Should().Be(1);
        loop.State.CurrentBigRound.Should().Be(1);

        loop.EndPlayerTurn();
        loop.State.CurrentPlayerIndex.Should().Be(0);
        loop.State.CurrentBigRound.Should().Be(2);
    }

    [Fact]
    public void EndPlayerTurn_ZeroesActionPoints()
    {
        var (loop, _) = Setup();
        loop.State.Players[0].ActionPoints = 3;
        loop.EndPlayerTurn();
        loop.State.Players[0].ActionPoints.Should().Be(0);
    }

    [Fact]
    public void PlayCard_ConsumesActionPoints_AndUsesDice()
    {
        var (loop, module) = Setup();
        loop.Advance();                      // Draw → Action (fills hand, AP=3)

        var player = loop.State.CurrentPlayer;
        var cardId = player.Hand.First();
        var card = module.ActionCards[cardId];
        int apBefore = player.ActionPoints;
        int handCountBefore = player.Hand.Count;

        loop.PlayCard(cardId, Stat.Skill, tn: 8);

        player.ActionPoints.Should().Be(apBefore - card.Cost);
        player.Hand.Should().HaveCount(handCountBefore - 1);
        loop.LastRoll.Should().NotBeNull();
    }

    [Fact]
    public void TurnLoop_UsesSeededDice_DeterministicAcrossRuns()
    {
        string RunSnapshot(int seed)
        {
            var (loop, _) = Setup(seed);
            for (int turn = 0; turn < 6; turn++)
            {
                for (int phase = 0; phase < 5; phase++) loop.Advance();
                loop.EndPlayerTurn();
            }
            return $"round={loop.State.CurrentBigRound};player={loop.State.CurrentPlayerIndex};lastRoll={loop.LastRoll}";
        }
        RunSnapshot(1234).Should().Be(RunSnapshot(1234));
    }

    [Fact]
    public void IsGameOver_WhenBigRoundExceedsTurnLimit_ReturnsTrue()
    {
        var (loop, _) = Setup();
        loop.State.CurrentBigRound = loop.State.MaxBigRounds + 1;
        loop.IsGameOver(out var reason).Should().BeTrue();
        reason.Should().Be(EndReason.TurnLimitReached);
    }

    [Fact]
    public void IsGameOver_WhenAllPlayersHpZero_ReturnsAllPlayersDown()
    {
        var (loop, _) = Setup();
        foreach (var p in loop.State.Players) p.Hp = 0;
        loop.IsGameOver(out var reason).Should().BeTrue();
        reason.Should().Be(EndReason.AllPlayersDown);
    }
}
