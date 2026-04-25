using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using Xunit;

namespace CardNarrative.Tests.Services;

public class BattleEnginePlayerActionTests
{
    private static (BattleEngine engine, BattleCard card, PlayerState player, Character character, Module module, BattleState bs, GameState state)
        Setup(params RollResult[] rolls)
    {
        var module = ModuleFactory.Load();
        var card = module.Battles["warehouse-guard"];
        var state = ModuleFactory.NewState(module, players: 1);
        var player = state.Players[0];
        var character = module.Characters[player.CharacterId];
        var engine = new BattleEngine(new FakeDiceService(rolls));
        var bs = engine.Start(card);
        bs.Phase = BattlePhase.PlayerTurn;
        return (engine, card, player, character, module, bs, state);
    }

    [Fact]
    public void BasicAttack_MarksBasicUsed_AndDealsDamage()
    {
        // wild-hunter Power=3, roll=6+5=11 → total 14 vs evasion 8 → Success; enemy HP 20 → reduced.
        var (engine, card, player, character, module, bs, state) =
            Setup(new RollResult(6, 5));
        int hp0 = bs.EnemyHp;

        var res = engine.ResolvePlayerAction(bs, card, player, character,
            new BasicActionChoice(BasicActionKind.Attack), state, module);

        res.Degree.Should().Be(CheckDegree.Success);
        res.DamageDealt.Should().BeGreaterThan(0);
        bs.EnemyHp.Should().BeLessThan(hp0);
        bs.UsedBasicActionThisTurn.Should().Contain(0);
    }

    [Fact]
    public void BasicDefend_GrantsEvasionBonusThisRound()
    {
        var (engine, card, player, character, module, bs, state) = Setup();

        var res = engine.ResolvePlayerAction(bs, card, player, character,
            new BasicActionChoice(BasicActionKind.Defend), state, module);

        res.EvasionGranted.Should().Be(2);
        bs.PlayerEvasionBonusThisRound[0].Should().Be(2);
    }

    [Fact]
    public void BasicReposition_AddsDodgeBonusNext()
    {
        var (engine, card, player, character, module, bs, state) = Setup();

        var res = engine.ResolvePlayerAction(bs, card, player, character,
            new BasicActionChoice(BasicActionKind.Reposition), state, module);

        res.DodgeBonusGranted.Should().Be(2);
        bs.PlayerDodgeBonusNext[0].Should().Be(2);
    }

    [Fact]
    public void BasicRetreat_EndsBattle_Fled()
    {
        var (engine, card, player, character, module, bs, state) = Setup();

        var res = engine.ResolvePlayerAction(bs, card, player, character,
            new BasicActionChoice(BasicActionKind.Retreat), state, module);

        res.Retreated.Should().BeTrue();
        bs.Phase.Should().Be(BattlePhase.EnemyFled);
    }

    [Fact]
    public void CardAction_ConsumesAp_AndMovesCardToDiscard()
    {
        // Player starts with 6 AP by default. Put 'basic-attack' in hand.
        var (engine, card, player, character, module, bs, state) =
            Setup(new RollResult(6, 5));
        player.Hand.Clear();
        player.Hand.Add("basic-attack");
        int apBefore = player.ActionPoints;

        engine.ResolvePlayerAction(bs, card, player, character,
            new CardActionChoice("basic-attack"), state, module);

        player.ActionPoints.Should().Be(apBefore - 1);
        player.Hand.Should().NotContain("basic-attack");
        player.Discard.Should().Contain("basic-attack");
    }

    [Fact]
    public void CardAction_ThrowsWhenNotInHand()
    {
        var (engine, card, player, character, module, bs, state) = Setup();
        player.Hand.Clear();
        Action act = () => engine.ResolvePlayerAction(bs, card, player, character,
            new CardActionChoice("basic-attack"), state, module);
        act.Should().Throw<InvalidOperationException>();
    }
}
