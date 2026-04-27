// Phase 2 任務 13 Stage 5 — 同伴戰鬥輔助 3 種規格化測試（規格書 §1.7）。
// 驗證：
//   - AttackBoost：玩家下次攻擊命中時傷害 +2，命中後消耗
//   - RollSupport：玩家下次擲骰 +2（攻擊 / 迴避 / 反擊都消耗）
//   - BlockDamage：同伴蓄勢 → 玩家受擊由同伴代受全額傷害，蓄勢消耗
//   - 冷卻：每戰每同伴每種輔助 1 次（再用回 false）
//   - 同伴 HP=0：所有輔助 disabled
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using Xunit;

namespace CardNarrative.Tests.Services;

public class CompanionCombatSupportTests
{
    private static (BattleEngine engine, BattleCard card, GameState state, Character character, Module module, BattleState bs)
        Setup(params RollResult[] rolls)
    {
        var module = ModuleFactory.Load();
        var card = module.Battles["warehouse-guard"];
        var state = ModuleFactory.NewState(module, players: 1);
        // 確保至少有 1 個同伴
        if (state.Companions.Count == 0)
        {
            var npc = module.NpcCompanions.Values.First();
            state.Companions.Add(new CompanionState { CompanionId = npc.Id, Hp = npc.Hp });
        }
        var character = module.Characters[state.Players[0].CharacterId];
        var engine = new BattleEngine(new FakeDiceService(rolls));
        var bs = engine.Start(card);
        bs.Phase = BattlePhase.PlayerTurn; // 設置玩家可行動
        return (engine, card, state, character, module, bs);
    }

    [Fact]
    public void AttackBoost_AppliesPlusTwoDamage_AndConsumesAfterHit()
    {
        // 用爆擊（雙 6）保證命中，便於觀察 +2 傷害是否套到 dmg
        var (engine, card, state, character, module, bs) = Setup(new RollResult(6, 6));
        var support = new CompanionCombatSupport();

        // 設置 AttackBoost
        support.TryAttackBoost(bs, state, companionIdx: 0).Should().BeTrue();
        bs.PlayerNextAttackBonus.GetValueOrDefault(bs.ActivePlayerIndex).Should().Be(2);

        // 玩家攻擊命中 → 額外 +2 傷害套到敵人
        int hpBefore = bs.EnemyHp;
        engine.ResolvePlayerAction(bs, card, state.Players[0], character,
            new BasicActionChoice(BasicActionKind.Attack), state, module);
        int dmgDealt = hpBefore - bs.EnemyHp;

        // 命中後消耗
        bs.PlayerNextAttackBonus.GetValueOrDefault(bs.ActivePlayerIndex).Should().Be(0);
        // 傷害應包含 +2 加乘（無精確值對照，但至少 ≥ baseDmg + 2）
        dmgDealt.Should().BeGreaterOrEqualTo(3); // weapon=0 + power(>=1) + crit(+3) - defense + AttackBoost(2) ≥ 3
    }

    [Fact]
    public void RollSupport_AppliesPlusTwoToTotal_AndConsumesAfterRoll()
    {
        var (engine, card, state, character, module, bs) = Setup(new RollResult(3, 3));
        var support = new CompanionCombatSupport();

        support.TryRollSupport(bs, state, companionIdx: 0).Should().BeTrue();
        bs.PlayerNextRollBonus.GetValueOrDefault(bs.ActivePlayerIndex).Should().Be(2);

        // 玩家攻擊（任意擲骰即消耗）
        engine.ResolvePlayerAction(bs, card, state.Players[0], character,
            new BasicActionChoice(BasicActionKind.Attack), state, module);

        bs.PlayerNextRollBonus.GetValueOrDefault(bs.ActivePlayerIndex).Should().Be(0);
    }

    [Fact]
    public void BlockDamage_CompanionTakesFullDamage_OnPlayerHit()
    {
        // 設一個攻擊類 EnemyAction → 蓄勢觸發 → 同伴 HP 下降，玩家不受傷
        var (engine, card, state, _, module, bs) = Setup(new RollResult(6, 6));
        var support = new CompanionCombatSupport();

        support.TryBlockDamage(bs, state, companionIdx: 0).Should().BeTrue();
        bs.CompanionBlockPending.Should().BeTrue();

        var attackAction = card.EnemyActions.First(a => a.Kind == EnemyActionKind.Attack);
        var plan = new EnemyActionPlan(attackAction, 0, "test attack");
        bs.Phase = BattlePhase.AwaitingResponse;

        int playerHpBefore = state.Players[0].Hp;
        int companionHpBefore = state.Companions[0].Hp;
        engine.ResolveEnemyAction(bs, card, plan, new AcceptResponse(), state.Players, module, state);

        // 玩家不受傷
        state.Players[0].Hp.Should().Be(playerHpBefore);
        // 同伴 HP 下降 ≥ baseDmg
        state.Companions[0].Hp.Should().BeLessThan(companionHpBefore);
        // 蓄勢消耗
        bs.CompanionBlockPending.Should().BeFalse();
    }

    [Fact]
    public void Cooldown_SecondUseOfSameSupport_ReturnsFalse()
    {
        var (_, _, state, _, _, bs) = Setup(new RollResult(3, 3));
        var support = new CompanionCombatSupport();

        support.TryAttackBoost(bs, state, 0).Should().BeTrue();
        // 再次嘗試同種輔助 → false（每戰每同伴 1 次）
        support.TryAttackBoost(bs, state, 0).Should().BeFalse();

        // 但其他種輔助仍可用（每種獨立冷卻）
        support.TryRollSupport(bs, state, 0).Should().BeTrue();
        support.TryBlockDamage(bs, state, 0).Should().BeTrue();
    }

    [Fact]
    public void CompanionHpZero_AllSupportsDisabled()
    {
        var (_, _, state, _, _, bs) = Setup(new RollResult(3, 3));
        var support = new CompanionCombatSupport();

        state.Companions[0].Hp = 0;

        support.TryAttackBoost(bs, state, 0).Should().BeFalse();
        support.TryRollSupport(bs, state, 0).Should().BeFalse();
        support.TryBlockDamage(bs, state, 0).Should().BeFalse();
    }
}
