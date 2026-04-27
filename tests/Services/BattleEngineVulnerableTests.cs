// Phase 2 任務 13 Stage 3 — vulnerable 持續效果規格化測試（規格書 §1.11）。
// 驗證 BattleState.PlayerNextHitBonusDamage 行為符合 §1.11 規格：
//   - 雙 1（ResolveEncounter）觸發 +2
//   - CritFail（RollAttack）觸發 +2
//   - stacking=replace（多次觸發 +2 不疊加為 +4）
//   - 受擊一次後消耗（reset 為 0）
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using Xunit;

namespace CardNarrative.Tests.Services;

public class BattleEngineVulnerableTests
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
    public void Vulnerable_Encounter_DoubleOne_AddsTwoBonusDamage()
    {
        var (engine, card, player, character, module, bs) = Setup(new RollResult(1, 1));
        engine.ResolveEncounter(bs, card, player, character, module);

        bs.PlayerNextHitBonusDamage.GetValueOrDefault(bs.ActivePlayerIndex).Should().Be(2);
    }

    [Fact]
    public void Vulnerable_StackingReplace_DoubleOnePlusCritFail_StaysAtTwo()
    {
        // §1.11 stacking=replace：雙 1 已寫入 +2 → CritFail 試圖再寫入 +2 → 結果仍為 2，不疊加為 4
        var (engine, card, player, character, module, bs) =
            Setup(new RollResult(1, 1),  // ResolveEncounter 雙 1
                  new RollResult(1, 1)); // RollAttack CritFail
        engine.ResolveEncounter(bs, card, player, character, module);
        bs.PlayerNextHitBonusDamage.GetValueOrDefault(bs.ActivePlayerIndex).Should().Be(2);

        // 強制設定 Phase 為 PlayerTurn 讓 RollAttack 可呼叫（ResolveEncounter Ambushed 會切到 EnemyTurn）
        bs.Phase = BattlePhase.PlayerTurn;
        engine.ResolvePlayerAction(bs, card, player, character,
            new BasicActionChoice(BasicActionKind.Attack), null, module);

        // 第二次觸發 vulnerable 不疊加，仍為 2
        bs.PlayerNextHitBonusDamage.GetValueOrDefault(bs.ActivePlayerIndex).Should().Be(2);
    }

    [Fact]
    public void Vulnerable_ConsumedAfterOneHit_ResetsToZero()
    {
        // 模擬：玩家有 vulnerable +2 → 敵方攻擊命中 → 額外 +2 傷害套用 → 隨即清零
        var (engine, card, player, character, module, bs) =
            Setup(new RollResult(1, 1),  // ResolveEncounter 設置 vulnerable
                  new RollResult(6, 6)); // 敵方攻擊命中
        engine.ResolveEncounter(bs, card, player, character, module);
        bs.PlayerNextHitBonusDamage.GetValueOrDefault(bs.ActivePlayerIndex).Should().Be(2);

        // 設一個有攻擊的敵方 EnemyAction
        var attackAction = card.EnemyActions.First(a => a.Kind == EnemyActionKind.Attack);
        var plan = new EnemyActionPlan(attackAction, bs.ActivePlayerIndex, "test attack");
        bs.PendingEnemyAction = attackAction;
        bs.PendingResponseTargetPlayerIndex = bs.ActivePlayerIndex;
        bs.Phase = BattlePhase.AwaitingResponse;

        int hpBefore = player.Hp;
        engine.ResolveEnemyAction(bs, card, plan, new AcceptResponse(), new[] { player }, module);
        int dmgTaken = hpBefore - player.Hp;

        // vulnerable 已消耗 → 清零
        bs.PlayerNextHitBonusDamage.GetValueOrDefault(bs.ActivePlayerIndex).Should().Be(0);
        // 受擊傷害 = raw payload + 2 vulnerable bonus
        dmgTaken.Should().BeGreaterOrEqualTo(attackAction.Payload.Damage + 2);
    }
}
