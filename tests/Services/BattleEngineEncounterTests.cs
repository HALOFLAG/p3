using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using Xunit;

namespace CardNarrative.Tests.Services;

public class BattleEngineEncounterTests
{
    private static (BattleEngine engine, BattleCard card, PlayerState player, Character character, Module module, BattleState bs)
        Setup(params RollResult[] rolls)
    {
        var module = ModuleFactory.Load();
        var card = module.Battles["warehouse-guard"];
        var state = ModuleFactory.NewState(module, players: 1);
        var player = state.Players[0];
        var character = module.Characters[player.CharacterId];
        var engine = new BattleEngine(new FakeDiceService(rolls));
        var bs = engine.Start(card);
        return (engine, card, player, character, module, bs);
    }

    [Fact]
    public void Encounter_DoubleSix_Advantage_FullReveal_PlayerFirst()
    {
        var (engine, card, player, character, module, bs) =
            Setup(new RollResult(6, 6));
        var res = engine.ResolveEncounter(bs, card, player, character, module);

        res.Tier.Should().Be(EncounterTier.Advantage);
        res.Reveal.Should().Be(RevealLevel.Full);
        res.PlayerGoesFirst.Should().BeTrue();
        bs.Phase.Should().Be(BattlePhase.PlayerTurn);
    }

    [Fact]
    public void Encounter_NormalTotal_PartialReveal_PlayerFirst()
    {
        // TN=9, Skill=4(wild-hunter) + 6 roll = 10 ≥ 9 but < 12 (not advantage).
        var (engine, card, player, character, module, bs) =
            Setup(new RollResult(3, 3));
        var res = engine.ResolveEncounter(bs, card, player, character, module);

        res.Tier.Should().Be(EncounterTier.Normal);
        res.Reveal.Should().Be(RevealLevel.Partial);
        res.PlayerGoesFirst.Should().BeTrue();
        bs.Phase.Should().Be(BattlePhase.PlayerTurn);
    }

    [Fact]
    public void Encounter_LowRoll_Ambushed_EnemyFirst_ApPenaltyPending()
    {
        // Skill=4, roll=1+1 = 6 -> total 10? Actually double1 forces Ambushed + +2 next-hit bonus.
        var (engine, card, player, character, module, bs) =
            Setup(new RollResult(1, 1));
        var res = engine.ResolveEncounter(bs, card, player, character, module);

        res.Tier.Should().Be(EncounterTier.Ambushed);
        res.Reveal.Should().Be(RevealLevel.None);
        res.PlayerGoesFirst.Should().BeFalse();
        bs.Phase.Should().Be(BattlePhase.EnemyTurn);
        bs.PendingApPenalty[0].Should().Be(1);
        bs.PlayerNextHitBonusDamage.GetValueOrDefault(0).Should().Be(2);
    }

    [Fact]
    public void Encounter_LowTotal_Ambushed_NoDouble1Bonus()
    {
        // wild-hunter Skill=4; roll=1+2=3 → total 7 < 9, Ambushed, not double1.
        var (engine, card, player, character, module, bs) =
            Setup(new RollResult(1, 2));
        var res = engine.ResolveEncounter(bs, card, player, character, module);

        res.Tier.Should().Be(EncounterTier.Ambushed);
        bs.PendingApPenalty[0].Should().Be(1);
        bs.PlayerNextHitBonusDamage.GetValueOrDefault(0).Should().Be(0);
    }
}
