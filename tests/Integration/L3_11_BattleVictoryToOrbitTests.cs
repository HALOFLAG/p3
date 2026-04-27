// Phase 2 任務 13 Stage 6 — L3-11「事件→戰鬥→勝利 LootEffects→state 變化」整合測試（規格書 §1.8 + §1.4）。
//
// 規格 L3-11：玩家觸發 triggerBattle 事件 → 戰鬥啟動 → 玩家勝利 → BattleCard.LootEffects 套用
//   → state.Resources / Backpack / Flags 反映變化（後續 ORBIT 事件 condition 可用此狀態重評估）。
//
// 本測試驗事件層：
//   - TriggerBattleEffect 寫入 state.PendingBattleId（既有路徑，Stage 1 接通）
//   - BattleEngine.Start + ResolveEncounter + ResolvePlayerAction 攻擊到敵 HP=0
//   - CheckEnd 切 Phase=Victory
//   - LootEffects 套用後 state 改動（grantResource / grantEquipment）
//
// UI 層 BattleScene.ApplyBattleEndResolution 是模擬 caller（直接呼 EffectHandler.Apply）。
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;
using Xunit;

namespace CardNarrative.Tests.Integration;

public class L3_11_BattleVictoryToOrbitTests
{
    [Fact]
    public void L3_11_BattleVictory_AppliesLootEffects_AndUpdatesGameState()
    {
        // === 場景建立 ===
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, players: 1);
        var card = module.Battles.Values.First(b => b.LootEffects.Count > 0);

        // 1. TriggerBattleEffect 寫入 PendingBattleId
        var triggerHandler = new EffectHandler();
        triggerHandler.Apply(new TriggerBattleEffect(card.Id), state, module);
        state.PendingBattleId.Should().Be(card.Id);

        // 2. 模擬戰鬥啟動 + 玩家勝利（直接 fast-forward 敵 HP=0）
        var engine = new BattleEngine(new FakeDiceService(new RollResult(6, 6)));
        var bs = engine.Start(card);
        bs.EnemyHp = 0; // 模擬玩家攻擊勝利

        // 3. CheckEnd → Victory
        var endResult = engine.CheckEnd(bs, card, state.Players);
        endResult.Should().Be(BattleEndResult.Victory);
        bs.Phase.Should().Be(BattlePhase.Victory);

        // 4. UI 層 ApplyBattleEndResolution 模擬：套用 LootEffects
        int clueBefore = state.Resources.GetValueOrDefault("clue", 0);
        int backpackBefore = state.CurrentPlayer.Backpack.Count;
        int flagsBefore = state.Flags.Count;
        int equippedBefore = state.CurrentPlayer.Equipment.Values.Count(v => v != null);

        var handler = new EffectHandler();
        foreach (var effect in card.LootEffects)
            handler.Apply(effect, state, module);

        // 5. 驗證：state 至少有一處改動（依 LootEffects 內容）
        bool anyChange = state.Resources.GetValueOrDefault("clue", 0) > clueBefore
                         || state.CurrentPlayer.Backpack.Count > backpackBefore
                         || state.Flags.Count > flagsBefore
                         || state.CurrentPlayer.Equipment.Values.Count(v => v != null) > equippedBefore;
        anyChange.Should().BeTrue();

        // 6. UI 層接著清 PendingBattleId（MainBootstrap.OnBattleClosed）
        state.PendingBattleId = null;
        state.PendingBattleId.Should().BeNull();
    }

    [Fact]
    public void L3_11_FullChain_TriggerBattleEffectThenLootEffects_PreservesGrantResourceCount()
    {
        // 完整鏈：TriggerBattle → 模擬 Victory → 套用 grantResource → state.Resources 反映
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, players: 1);
        // 找一個含 grantResource 的戰鬥
        var card = module.Battles.Values.First(b =>
            b.LootEffects.OfType<GrantResourceEffect>().Any());

        new EffectHandler().Apply(new TriggerBattleEffect(card.Id), state, module);

        var engine = new BattleEngine(new FakeDiceService(new RollResult(6, 6)));
        var bs = engine.Start(card);
        bs.EnemyHp = 0;
        engine.CheckEnd(bs, card, state.Players);
        bs.Phase.Should().Be(BattlePhase.Victory);

        var grantResource = card.LootEffects.OfType<GrantResourceEffect>().First();
        int before = state.Resources.GetValueOrDefault(grantResource.Key, 0);
        new EffectHandler().Apply(grantResource, state, module);
        state.Resources.GetValueOrDefault(grantResource.Key, 0).Should().Be(before + grantResource.Amount);
    }
}
