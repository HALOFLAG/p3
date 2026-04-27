// CompanionCombatSupport — 戰鬥中同伴 3 種輔助（規格書 §1.7）。
// 任務 13 Stage 5：每種輔助每戰每同伴 1 次冷卻；同伴 HP=0 時禁用。
//   AttackBoost   — 設定 BattleState.PlayerNextAttackBonus；命中後消耗。
//   RollSupport   — 設定 BattleState.PlayerNextRollBonus；任意擲骰後消耗。
//   BlockDamage   — 設定 BattleState.CompanionBlockPending；受擊時由同伴代受全額。
// Phase 2 簡化：不嚴格扣同伴 ActionPoints（規格 §1.7 寫 1 AP）— 用「每戰 1 次冷卻」近似；同伴 AP 留 Phase 3 補完。
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public sealed class CompanionCombatSupport
{
    public const int AttackBoostAmount = 2;
    public const int RollSupportAmount = 2;

    /// <summary>玩家下次攻擊命中時傷害 +2；命中後 BattleState.PlayerNextAttackBonus 重置 0。</summary>
    public bool TryAttackBoost(BattleState bs, GameState state, int companionIdx)
    {
        if (!CanUseSupport(state, companionIdx, CompanionCombatSupportKind.AttackBoost)) return false;
        int playerIdx = state.CurrentPlayerIndex;
        bs.PlayerNextAttackBonus[playerIdx] = Math.Max(
            bs.PlayerNextAttackBonus.GetValueOrDefault(playerIdx), AttackBoostAmount);
        ConsumeSupport(state, companionIdx, CompanionCombatSupportKind.AttackBoost);
        return true;
    }

    /// <summary>玩家下次擲骰（攻擊 / 迴避 / 格擋 / 反擊）+2；任意擲骰後 PlayerNextRollBonus 重置 0。</summary>
    public bool TryRollSupport(BattleState bs, GameState state, int companionIdx)
    {
        if (!CanUseSupport(state, companionIdx, CompanionCombatSupportKind.RollSupport)) return false;
        int playerIdx = state.CurrentPlayerIndex;
        bs.PlayerNextRollBonus[playerIdx] = Math.Max(
            bs.PlayerNextRollBonus.GetValueOrDefault(playerIdx), RollSupportAmount);
        ConsumeSupport(state, companionIdx, CompanionCombatSupportKind.RollSupport);
        return true;
    }

    /// <summary>同伴蓄勢，玩家下次受擊由此同伴代受全額傷害；觸發後 CompanionBlockPending 重置 false。</summary>
    public bool TryBlockDamage(BattleState bs, GameState state, int companionIdx)
    {
        if (!CanUseSupport(state, companionIdx, CompanionCombatSupportKind.BlockDamage)) return false;
        bs.CompanionBlockPending = true;
        bs.CompanionBlockSourceIdx = companionIdx;
        ConsumeSupport(state, companionIdx, CompanionCombatSupportKind.BlockDamage);
        return true;
    }

    /// <summary>檢查指定同伴是否可使用該輔助（HP&gt;0 + 該輔助本戰未用過）。</summary>
    public static bool CanUseSupport(GameState state, int companionIdx, CompanionCombatSupportKind kind)
    {
        if (companionIdx < 0 || companionIdx >= state.Companions.Count) return false;
        var c = state.Companions[companionIdx];
        if (c.Hp <= 0) return false;
        if (c.UsedCombatSupportThisBattle.Contains(kind)) return false;
        return true;
    }

    private static void ConsumeSupport(GameState state, int companionIdx, CompanionCombatSupportKind kind)
    {
        state.Companions[companionIdx].UsedCombatSupportThisBattle.Add(kind);
    }
}
