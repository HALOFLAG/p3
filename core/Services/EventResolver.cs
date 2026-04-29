// EventResolver — 事件檢定結算：擲骰 + Stat + 裝備加值 vs 最終 TN（含地塊修正/climax）。
// Tier：Success / PartialSuccess（差 1~2） / Failure；套對應 EventOutcome.Effects。
// 由 UI（EventResolutionViewModel）或 TurnLoop 呼叫；EffectHandler 套用後續效果。
using System.Text.Json.Serialization;
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

/// <summary>
/// 事件結算分級。Phase 3 任務 14（S8）起掛 <see cref="JsonStringEnumConverter"/>，
/// 序列化為 "success" / "partialSuccess" / "failure"，與 JsonLogic context
/// event.&lt;id&gt;.outcome 字串值一致；後續 SaveService 載入舊存檔時 enum 增值不破檔。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventOutcomeTier
{
    Success,
    PartialSuccess,
    Failure
}

public sealed record EventResolution(
    EventCard Event,
    Stat Stat,
    int StatValue,
    RollResult Roll,
    int Total,
    int TnUsed,
    EventOutcomeTier Tier,
    EventOutcome Outcome
);

public sealed class EventResolver
{
    private readonly IDiceService _dice;
    private readonly IEffectHandler _effects;
    private readonly ITurnLogSink? _log;
    private readonly CompanionAbilityHandler? _companionAbilities;

    public EventResolver(IDiceService dice, IEffectHandler effects, ITurnLogSink? log = null,
        CompanionAbilityHandler? companionAbilities = null)
    {
        _dice = dice;
        _effects = effects;
        _log = log;
        _companionAbilities = companionAbilities;
    }

    public EventResolution Resolve(EventCard card, GameState state, Module module, EventOutcomeTier? forceTier = null)
    {
        var player = state.CurrentPlayer;
        var character = module.Characters[player.CharacterId];
        int statValue = card.Stat switch
        {
            Stat.Power     => character.Stats.Power,
            Stat.Social    => character.Stats.Social,
            Stat.Skill     => character.Stats.Skill,
            Stat.Intellect => character.Stats.Intellect,
            _ => 0
        };

        int tn = TileModifierService.ComputeEventTn(module, state, card);
        var roll = _dice.Roll2d6();
        // A3 · 納入 Specialty 擲骰加值；NextRoll 條目用一次後清除
        int specialtyBonus = state.PendingRollBonuses.Sum(b => b.Amount);
        int total = roll.Total + statValue + specialtyBonus;
        state.PendingRollBonuses.RemoveAll(
            b => b.Duration == SpecialtyEffectDuration.NextRoll);

        EventOutcomeTier tier;
        EventOutcome outcome;
        if (forceTier is { } ft)
        {
            tier = ft;
            outcome = ft switch
            {
                EventOutcomeTier.Success => card.Outcomes.Success,
                EventOutcomeTier.PartialSuccess => card.Outcomes.PartialSuccess,
                _ => card.Outcomes.Failure
            };
        }
        else if (total >= tn)
        {
            tier = EventOutcomeTier.Success;
            outcome = card.Outcomes.Success;
        }
        else if (total >= tn - 3)
        {
            tier = EventOutcomeTier.PartialSuccess;
            outcome = card.Outcomes.PartialSuccess;
        }
        else
        {
            tier = EventOutcomeTier.Failure;
            outcome = card.Outcomes.Failure;
        }

        foreach (var e in outcome.Effects)
            _effects.Apply(e, state, module);

        // §13-4 · 結算 Success 時累加當前玩家貢獻次數；結局評分
        // `contributions * 2 + equipmentSlotsFilled` 於 EndingEvaluator 才會有非 0 值。
        if (tier == EventOutcomeTier.Success)
            player.Contributions++;

        state.ConsumedEventIds.Add(card.Id);
        // Phase 3 任務 14（S3）· 同步寫結果分級給 JsonLogic 條件 (event.<id>.outcome) 用。
        state.EventOutcomes[card.Id] = tier;

        // A3 · OnEventResolved 觸發 Specialty（例：結束後續發效果）+ 清理 ThisEvent bonus
        _companionAbilities?.Fire(SpecialtyTrigger.OnEventResolved, state, module, card);

        var tierLabel = tier switch
        {
            EventOutcomeTier.Success => "成功",
            EventOutcomeTier.PartialSuccess => "部分成功",
            _ => "失敗"
        };
        var logKind = tier switch
        {
            EventOutcomeTier.Success => TurnLogKind.Hit,
            EventOutcomeTier.Failure => TurnLogKind.Miss,
            _ => TurnLogKind.Neutral
        };
        _log?.Append(
            $"事件「{card.Name}」：2d6={roll.Total}＋{card.Stat}={statValue} = {total} vs TN {tn} → {tierLabel}。",
            logKind);

        return new EventResolution(card, card.Stat, statValue, roll, total, tn, tier, outcome);
    }
}
