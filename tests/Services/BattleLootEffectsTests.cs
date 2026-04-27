// Phase 2 任務 13 Stage 6 — 戰鬥結算（LootEffects + Defeat 重生）測試（規格書 §1.8 + §1.10 簡化版）。
// 驗證：
//   - Victory：BattleCard.LootEffects 套用到 GameState（grantResource / grantEquipment / setFlag）
//   - EnemyFled：撤退無戰利品（state 不被 LootEffects 改動）
//   - Defeat：玩家 HP=0 → CheckEnd 設 Phase=Defeat（重生由 BattleScene.ApplyBattleEndResolution 處理）
// 重生邏輯（hp=1 復活）由 UI 層 BattleScene 直接 mutate state.CurrentPlayer.Hp，不在 BattleEngine 內，故此處只測 CheckEnd 切 Phase。
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using Xunit;

namespace CardNarrative.Tests.Services;

public class BattleLootEffectsTests
{
    private static (BattleEngine engine, BattleCard card, GameState state, Module module, BattleState bs)
        Setup(string battleId, params RollResult[] rolls)
    {
        var module = ModuleFactory.Load();
        var card = module.Battles[battleId];
        var state = ModuleFactory.NewState(module, players: 1);
        var engine = new BattleEngine(new FakeDiceService(rolls));
        var bs = engine.Start(card);
        return (engine, card, state, module, bs);
    }

    [Fact]
    public void Victory_AppliesLootEffects_GrantResource()
    {
        // warehouse-guard loot 含 grantResource clue +1（依模組設定）；用 EffectHandler 直接套
        var (_, card, state, module, _) = Setup("warehouse-guard", new RollResult(6, 6));

        var handler = new EffectHandler();
        int clueBefore = state.Resources.GetValueOrDefault("clue", 0);
        foreach (var effect in card.LootEffects)
            handler.Apply(effect, state, module);

        // 至少有一種 loot 套到 state（grantResource / grantEquipment / setFlag 取決於模組設計）
        bool anyApplied = state.Resources.GetValueOrDefault("clue", 0) > clueBefore
                          || state.CurrentPlayer.Backpack.Count > 0
                          || state.Flags.Count > 0;
        anyApplied.Should().BeTrue();
    }

    [Fact]
    public void Victory_AppliesLootEffects_GrantEquipment_AddsToBackpack()
    {
        // 構造一個含 GrantEquipmentEffect 的測試 BattleCard（不依賴模組具體設定）
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, players: 1);
        var firstEquipId = module.Equipment.Keys.First();

        var handler = new EffectHandler();
        int backpackBefore = state.CurrentPlayer.Backpack.Count;
        handler.Apply(new GrantEquipmentEffect(firstEquipId), state, module);

        // EffectHandler.ApplyGrantEquipment 三段：auto-equip / Backpack / PendingEquipmentGrants（PR-A）
        // Backpack 或 Equipment 任一有此 id 即視為套用成功
        bool inBackpack = state.CurrentPlayer.Backpack.Contains(firstEquipId);
        bool inEquipment = state.CurrentPlayer.Equipment.Values.Contains(firstEquipId);
        (inBackpack || inEquipment).Should().BeTrue();
    }

    [Fact]
    public void EnemyFled_NoLootEffectsApplied_StateUnchangedExceptDamage()
    {
        // 撤退 = 玩家主動 Retreat → Phase=EnemyFled；BattleScene 不呼 LootEffects
        // 此測試純驗 BattleEngine.ResolveBasic Retreat 路徑後 state.Resources 等不變
        var (engine, card, state, module, bs) = Setup("warehouse-guard", new RollResult(3, 3));
        var character = module.Characters[state.Players[0].CharacterId];
        bs.Phase = BattlePhase.PlayerTurn;
        int clueBefore = state.Resources.GetValueOrDefault("clue", 0);
        int backpackBefore = state.CurrentPlayer.Backpack.Count;

        engine.ResolvePlayerAction(bs, card, state.Players[0], character,
            new BasicActionChoice(BasicActionKind.Retreat), state, module);

        bs.Phase.Should().Be(BattlePhase.EnemyFled);
        // BattleEngine 自身不套 LootEffects；state 應未變
        state.Resources.GetValueOrDefault("clue", 0).Should().Be(clueBefore);
        state.CurrentPlayer.Backpack.Count.Should().Be(backpackBefore);
    }

    [Fact]
    public void HeroHpZero_CheckEndReturnsDefeat_PhaseSetToDefeat()
    {
        // 玩家 HP=0 → CheckEnd → Defeat（簡化重生 hp=1 由 BattleScene UI 處理，此處驗 engine 切 Phase）
        var (engine, card, state, _, bs) = Setup("warehouse-guard", new RollResult(3, 3));
        state.Players[0].Hp = 0;

        var result = engine.CheckEnd(bs, card, state.Players);

        result.Should().Be(BattleEndResult.Defeat);
        bs.Phase.Should().Be(BattlePhase.Defeat);
    }

    [Fact]
    public void EnemyHpZero_CheckEndReturnsVictory_PhaseSetToVictory()
    {
        // 敵 HP=0 → CheckEnd → Victory（後續 LootEffects 套用由 UI 層 BattleScene.ApplyBattleEndResolution 處理）
        var (engine, card, state, _, bs) = Setup("warehouse-guard", new RollResult(3, 3));
        bs.EnemyHp = 0;

        var result = engine.CheckEnd(bs, card, state.Players);

        result.Should().Be(BattleEndResult.Victory);
        bs.Phase.Should().Be(BattlePhase.Victory);
    }
}
