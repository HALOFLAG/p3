using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class TileProgressServiceTests
{
    private static (Module module, GameState state, Position pos) Setup(string tileId = "wilderness-path")
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var pos = new Position(1, 0);
        state.TileMap[(1, 0)] = new PlacedTile { TileId = tileId, Level = ExplorationLevel.Unknown };
        state.CurrentPlayer.Position = pos;
        return (module, state, pos);
    }

    [Fact]
    public void PromoteOnFirstEnter_PromotesUnknownToUnfamiliar()
    {
        var (_, state, _) = Setup();
        TileProgressService.PromoteOnFirstEnter(state.TileMap[(1, 0)]);
        state.TileMap[(1, 0)].Level.Should().Be(ExplorationLevel.Unfamiliar);
    }

    [Fact]
    public void ActionCard_AdvancesOnce_PerVisit()
    {
        var (module, state, pos) = Setup();
        state.TileMap[(1, 0)].Level = ExplorationLevel.Unfamiliar;

        var d1 = TileProgressService.Advance(state, module, pos, 1, ProgressReason.ActionCard);
        var d2 = TileProgressService.Advance(state, module, pos, 1, ProgressReason.ActionCard);

        d1.Should().Be(1);
        d2.Should().Be(0);
        state.TileMap[(1, 0)].Level.Should().Be(ExplorationLevel.Neutral);
    }

    [Fact]
    public void EventOutcome_CappedAtTwoPerBigRound()
    {
        var (module, state, pos) = Setup();
        state.TileMap[(1, 0)].Level = ExplorationLevel.Unfamiliar;

        var a = TileProgressService.Advance(state, module, pos, 1, ProgressReason.EventOutcome);
        var b = TileProgressService.Advance(state, module, pos, 1, ProgressReason.EventOutcome);
        var c = TileProgressService.Advance(state, module, pos, 1, ProgressReason.EventOutcome);

        a.Should().Be(1);
        b.Should().Be(1);
        c.Should().Be(0);
        state.TileMap[(1, 0)].Level.Should().Be(ExplorationLevel.Familiar);
    }

    [Fact]
    public void ImportantTile_CapsAtFamiliar()
    {
        var (module, state, pos) = Setup(tileId: "warehouse"); // warehouse.Important = true
        state.TileMap[(1, 0)].Level = ExplorationLevel.Familiar;

        var d = TileProgressService.Advance(state, module, pos, 1, ProgressReason.EventOutcome);

        d.Should().Be(0);
        state.TileMap[(1, 0)].Level.Should().Be(ExplorationLevel.Familiar);
    }

    [Fact]
    public void ClueInvestment_Spends2Clues_Grants1Progress()
    {
        var (module, state, pos) = Setup();
        state.TileMap[(1, 0)].Level = ExplorationLevel.Unfamiliar;
        state.Resources["clue_shard"] = 2;

        var ok = TileProgressService.TryInvestClues(state, module, pos);

        ok.Should().BeTrue();
        state.Resources["clue_shard"].Should().Be(0);
        state.TileMap[(1, 0)].Level.Should().Be(ExplorationLevel.Neutral);
    }

    [Fact]
    public void ClueInvestment_Fails_When_Clue_LessThan_2()
    {
        var (module, state, pos) = Setup();
        state.Resources["clue_shard"] = 1;
        var ok = TileProgressService.TryInvestClues(state, module, pos);
        ok.Should().BeFalse();
        state.Resources["clue_shard"].Should().Be(1);
    }

    [Fact]
    public void ResetBigRound_ClearsCounters_AndAllowsMoreProgress()
    {
        var (module, state, pos) = Setup();
        state.TileMap[(1, 0)].Level = ExplorationLevel.Unfamiliar;

        TileProgressService.Advance(state, module, pos, 1, ProgressReason.EventOutcome);
        TileProgressService.Advance(state, module, pos, 1, ProgressReason.EventOutcome);
        state.TileMap[(1, 0)].ProgressGainedThisBigRound.Should().Be(2);

        state.CurrentBigRound++;
        TileProgressService.ResetBigRound(state);

        state.TileMap[(1, 0)].ProgressGainedThisBigRound.Should().Be(0);
        var more = TileProgressService.Advance(state, module, pos, 1, ProgressReason.EventOutcome);
        more.Should().Be(1);
    }
}
