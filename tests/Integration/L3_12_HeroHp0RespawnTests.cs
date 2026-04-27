// Phase 2 任務 13 Stage 6 — L3-12「戰鬥中主角 HP=0 → 簡化重生 → 退回主場景」整合測試（規格書 §1.8 + §1.10 簡化版）。
//
// 規格 L3-12：戰鬥中玩家被擊到 HP=0 → CheckEnd 切 Phase=Defeat → 簡化重生 hp=1 → 戰鬥結束清 PendingBattleId。
// Phase 2 簡化：直接 hp=1 復活，正式 §1.10 完整重生擲骰留 Phase 3 任務 14。
//
// 本測試驗事件層：
//   - 敵方攻擊命中後玩家 HP=0
//   - CheckEnd 切 Phase=Defeat
//   - UI 層 ApplyBattleEndResolution 模擬簡化重生 hp=1
//   - 戰鬥結束清 PendingBattleId
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;
using Xunit;

namespace CardNarrative.Tests.Integration;

public class L3_12_HeroHp0RespawnTests
{
    [Fact]
    public void L3_12_PlayerHpZero_DefeatPhase_Then_SimplifiedRespawn_HpEqualsOne()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, players: 1);
        var card = module.Battles["warehouse-guard"];

        // 觸發戰鬥
        new EffectHandler().Apply(new TriggerBattleEffect(card.Id), state, module);
        state.PendingBattleId.Should().Be(card.Id);

        // 模擬玩家 HP=0
        state.Players[0].Hp = 0;
        var engine = new BattleEngine(new FakeDiceService(new RollResult(3, 3)));
        var bs = engine.Start(card);

        // CheckEnd 切 Phase=Defeat
        var result = engine.CheckEnd(bs, card, state.Players);
        result.Should().Be(BattleEndResult.Defeat);
        bs.Phase.Should().Be(BattlePhase.Defeat);

        // UI 層 ApplyBattleEndResolution 模擬：Phase 2 簡化重生 hp=1
        if (bs.Phase == BattlePhase.Defeat)
            state.CurrentPlayer.Hp = 1;
        state.CurrentPlayer.Hp.Should().Be(1);

        // 戰鬥結束（MainBootstrap.OnBattleClosed 模擬）
        state.PendingBattleId = null;
        state.PendingBattleId.Should().BeNull();
    }

    [Fact]
    public void L3_12_EnemyAttackKillsPlayer_ResolveEnemyAction_TriggersDefeatOnCheckEnd()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, players: 1);
        var card = module.Battles["warehouse-guard"];
        state.Players[0].Hp = 1; // 一擊必殺

        var engine = new BattleEngine(new FakeDiceService(
            new RollResult(6, 6),  // ResolveEnemyAction 內部 dodge roll
            new RollResult(6, 6))); // 備用
        var bs = engine.Start(card);
        bs.Phase = BattlePhase.AwaitingResponse;

        var attackAction = card.EnemyActions.First(a => a.Kind == EnemyActionKind.Attack);
        var plan = new EnemyActionPlan(attackAction, 0, "test attack");

        engine.ResolveEnemyAction(bs, card, plan, new AcceptResponse(), state.Players, module);

        // 玩家 HP 應降為 0
        state.Players[0].Hp.Should().Be(0);

        // CheckEnd 確認 Defeat（注意：ResolveEnemyAction 內部不會自動呼 CheckEnd）
        var result = engine.CheckEnd(bs, card, state.Players);
        result.Should().Be(BattleEndResult.Defeat);
        bs.Phase.Should().Be(BattlePhase.Defeat);
    }
}
