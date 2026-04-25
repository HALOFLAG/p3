// CompanionAbilityHandler — A3 · NPC Specialty 規則引擎。
// 接收 SpecialtyTrigger 事件（bigRoundStart / onBattleStart / onEventStart 等），
// 遍歷活躍同伴的 SpecialtyEffects，檢查 Condition 後執行 Effect。
// 由 TurnLoop / EventResolver / BattleEngine 各自在正確時機呼叫 Fire()。
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

/// <summary>A3 · 待生效的擲骰加值（由 BonusRollEffect 產生）。</summary>
public sealed class PendingRollBonus
{
    public int Amount { get; set; }
    public SpecialtyEffectDuration Duration { get; set; }
    public string SourceCompanionId { get; set; } = "";
}

public sealed class CompanionAbilityHandler
{
    private readonly ITurnLogSink? _log;

    public CompanionAbilityHandler(ITurnLogSink? log = null)
    {
        _log = log;
    }

    public void Fire(SpecialtyTrigger trigger, GameState state, Module module,
                     EventCard? eventContext = null)
    {
        foreach (var cs in state.Companions.Where(c => c.Hp > 0))
        {
            if (!module.NpcCompanions.TryGetValue(cs.CompanionId, out var npc)) continue;
            foreach (var binding in npc.SpecialtyEffects.Where(b => b.Trigger == trigger))
            {
                if (!Matches(binding.Condition, state, module, eventContext)) continue;
                Apply(binding.Effect, cs, npc, state);
            }
        }

        // 結算後自動清理過期 PendingRollBonus
        if (trigger == SpecialtyTrigger.OnEventResolved)
            state.PendingRollBonuses.RemoveAll(
                b => b.Duration == SpecialtyEffectDuration.ThisEvent);
        if (trigger == SpecialtyTrigger.OnBattleEnd)
            state.PendingRollBonuses.RemoveAll(
                b => b.Duration == SpecialtyEffectDuration.ThisBattle);
    }

    private static bool Matches(SpecialtyCondition? condition, GameState state, Module module,
                                EventCard? eventContext)
    {
        if (condition is null) return true;
        if (condition.EventTypeAny is { Count: > 0 } types)
        {
            if (eventContext is null) return false;
            if (!types.Contains(eventContext.Type)) return false;
        }
        if (condition.TileImportant is bool mustImportant || condition.TileTerrainAny is { })
        {
            var pos = state.CurrentPlayer.Position;
            if (!state.TileMap.TryGetValue((pos.X, pos.Y), out var placed)) return false;
            if (!module.Tiles.TryGetValue(placed.TileId, out var tile)) return false;
            if (condition.TileImportant is bool want && tile.Important != want) return false;
            if (condition.TileTerrainAny is { Count: > 0 } terrains
                && !terrains.Contains(tile.Terrain)) return false;
        }
        return true;
    }

    private void Apply(SpecialtyEffect effect, CompanionState cs, NpcCompanion npc,
                       GameState state)
    {
        switch (effect)
        {
            case HealLowestHpEffect h:
                var lowest = state.Players
                    .OrderBy(p => (double)p.Hp / Math.Max(1, p.HpMax))
                    .FirstOrDefault();
                if (lowest is null) return;
                int before = lowest.Hp;
                lowest.Hp = Math.Min(lowest.HpMax, lowest.Hp + h.Amount);
                if (before != lowest.Hp)
                    _log?.Append($"{npc.Name} 特長：隊友 HP {before} → {lowest.Hp}", TurnLogKind.Hit);
                break;

            case HealSelfEffect hs:
                int selfBefore = cs.Hp;
                cs.Hp = Math.Min(npc.Hp, cs.Hp + hs.Amount);
                if (selfBefore != cs.Hp)
                    _log?.Append($"{npc.Name} 特長：自身 HP {selfBefore} → {cs.Hp}", TurnLogKind.Hit);
                break;

            case BonusRollEffect br:
                state.PendingRollBonuses.Add(new PendingRollBonus
                {
                    Amount = br.Amount,
                    Duration = br.Duration,
                    SourceCompanionId = npc.Id
                });
                _log?.Append(
                    $"{npc.Name} 特長：擲骰 +{br.Amount}（{DescribeDuration(br.Duration)}）",
                    TurnLogKind.Neutral);
                break;

            case SpecialtyGrantResourceEffect gr:
                state.Resources[gr.Key] = state.Resources.GetValueOrDefault(gr.Key) + gr.Amount;
                _log?.Append($"{npc.Name} 特長：獲得 {gr.Key} ×{gr.Amount}", TurnLogKind.Neutral);
                break;
        }
    }

    private static string DescribeDuration(SpecialtyEffectDuration d) => d switch
    {
        SpecialtyEffectDuration.NextRoll => "下次擲骰",
        SpecialtyEffectDuration.ThisEvent => "本事件",
        SpecialtyEffectDuration.ThisBattle => "本戰鬥",
        _ => ""
    };
}
