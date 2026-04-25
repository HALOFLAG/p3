using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using Xunit;

namespace CardNarrative.Tests.Services;

public class BattleEngineEnemyActionTests
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
        bs.Phase = BattlePhase.EnemyTurn;
        return (engine, card, player, character, module, bs, state);
    }

    [Fact]
    public void PlanEnemyAction_PrefersFleeWhenHpLow()
    {
        // warehouse-guard: flee@hpAtMost 0.5 / attack@always
        var (engine, card, player, character, module, bs, state) = Setup();
        bs.EnemyHp = 5; // 5/20 = 0.25, triggers flee threshold

        var plan = engine.PlanEnemyAction(bs, card, state.Players);
        plan.Action.Kind.Should().Be(EnemyActionKind.Flee);
    }

    [Fact]
    public void PlanEnemyAction_DefaultsToAttackAtFullHp()
    {
        var (engine, card, player, character, module, bs, state) = Setup();
        var plan = engine.PlanEnemyAction(bs, card, state.Players);
        plan.Action.Kind.Should().Be(EnemyActionKind.Attack);
        plan.TargetPlayerIndex.Should().Be(0);
    }

    [Fact]
    public void AcceptResponse_AppliesFullDamage()
    {
        var (engine, card, player, character, module, bs, state) = Setup();
        int hpBefore = player.Hp;
        var action = card.EnemyActions.First(a => a.Kind == EnemyActionKind.Attack);
        var plan = new EnemyActionPlan(action, 0, "test");

        var res = engine.ResolveEnemyAction(bs, card, plan,
            new AcceptResponse(), state.Players, module, state);

        res.DamageDealtToPlayer.Should().BeGreaterThan(0);
        player.Hp.Should().BeLessThan(hpBefore);
    }

    [Fact]
    public void FleeAction_EndsBattle_NoResponseNeeded()
    {
        var (engine, card, player, character, module, bs, state) = Setup();
        var fleeAction = card.EnemyActions.First(a => a.Kind == EnemyActionKind.Flee);
        var plan = new EnemyActionPlan(fleeAction, 0, "test");

        engine.ResolveEnemyAction(bs, card, plan, new AcceptResponse(), state.Players, module, state);
        bs.Phase.Should().Be(BattlePhase.EnemyFled);
    }

    [Fact]
    public void DodgeResponse_SuccessfulRoll_TakesNoDamage()
    {
        // Skill=4, roll=6+6=12 → total 16 ≥ AtkPower (4) +2 buffer = full dodge.
        var (engine, card, player, character, module, bs, state) =
            Setup(new RollResult(6, 6));
        int hpBefore = player.Hp;
        var action = card.EnemyActions.First(a => a.Kind == EnemyActionKind.Attack);
        var plan = new EnemyActionPlan(action, 0, "test");

        var res = engine.ResolveEnemyAction(bs, card, plan,
            new DodgeResponse(), state.Players, module, state);

        res.Dodged.Should().BeTrue();
        res.DamageDealtToPlayer.Should().Be(0);
        player.Hp.Should().Be(hpBefore);
    }

    [Fact]
    public void DodgeResponse_ConsumesAp()
    {
        var (engine, card, player, character, module, bs, state) =
            Setup(new RollResult(6, 6));
        int apBefore = player.ActionPoints;
        var action = card.EnemyActions.First(a => a.Kind == EnemyActionKind.Attack);
        var plan = new EnemyActionPlan(action, 0, "test");

        engine.ResolveEnemyAction(bs, card, plan, new DodgeResponse(), state.Players, module, state);
        player.ActionPoints.Should().Be(apBefore - 1);
    }

    [Fact]
    public void CounterResponse_Success_DealsDamageToEnemy()
    {
        // Power=3 + 6+6=12 → total 15 ≥ atk 4 → success; reflects counter damage.
        var (engine, card, player, character, module, bs, state) =
            Setup(new RollResult(6, 6));
        int enemyHpBefore = bs.EnemyHp;
        var action = card.EnemyActions.First(a => a.Kind == EnemyActionKind.Attack);
        var plan = new EnemyActionPlan(action, 0, "test");

        var res = engine.ResolveEnemyAction(bs, card, plan,
            new CounterResponse(), state.Players, module, state);

        res.Dodged.Should().BeTrue();
        res.DamageReflectedToEnemy.Should().BeGreaterThan(0);
        bs.EnemyHp.Should().BeLessThan(enemyHpBefore);
    }
}
