// EventBroker — 事件觸發層（規格書 §1.6 + 任務 14 / Phase 3）。
//
// 取代散在 TurnLoop / WorldMap 的觸發呼叫，統一收 UI / 引擎送來的「玩家動作」訊號，
// 對 module.Events 做 Trigger 命中 + RevealCondition（JsonLogic）評估，
// 命中者透過 Sink 開結算對話框；同時命中多張時第一張開、其餘排入 PendingEventQueue。
//
// 三個入口（任務 14 全貌設計）：
//   - OnTileEnter(tileId)            地塊進入觸發（S1 起接通）
//   - OnPlayerAction(kind)           玩家行動觸發（S2 起接通；同時把 ActionCounts[kind]++）
//   - OnActionPhaseBegin(bigRound)   行動階段起始觸發（S2 起接通；turnAt/turnRange 命中）
//
// 排序：S1-S4 用 module 載入序（FirstOrDefault）；S5 起改用 prologue.eventPriorities。
// RevealCondition：S3 起啟用 — 不通過 reveal 的事件即使 trigger 命中也不會 fire（A1–A5 前置條件用）。
//
// Trigger 命中規則（S2）：
//   - TileEnterTrigger(id)            : 比對 OnTileEnter 參數
//   - TurnAtTrigger(N)                : OnActionPhaseBegin(round) 中 round == N
//   - TurnRangeTrigger(a, b?)         : OnActionPhaseBegin(round) 中 round >= a && (b==null || round <= b)
//   - PlayerActionTrigger(kind, n?)   : OnPlayerAction(k) 中 k == kind && (n==null || ActionCounts[kind] == n)
//   - FlagTrigger                     : 三入口皆重新評估（flag 隨時可變）
//   - TurnTimerTrigger / ActionCountTrigger : [Obsolete] runtime 忽略 + warn log
using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using CardNarrative.Core.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CS0618 // 為 obsolete trigger 留 warn-log 路徑

namespace CardNarrative.Core.Services;

/// <summary>EventBroker 命中事件後的回呼：UI 端開結算對話框。</summary>
public interface IEventBrokerSink
{
    void OnEventTriggered(EventCard card);
}

public sealed class EventBroker
{
    private readonly GameState _state;
    private readonly Module _module;
    private readonly JsonLogicEvaluator _evaluator;
    private readonly ILogger _log;
    /// <summary>
    /// Phase 3 任務 14（S5）· 同時命中多事件的排序索引（id → 優先序，數字越小越前）。
    /// 由 prologue.EventPriorities + module 載入序組成；建構時計算一次後重用。
    /// </summary>
    private readonly Dictionary<string, int> _priorityIndex;

    public EventBroker(GameState state, Module module, JsonLogicEvaluator? evaluator = null, ILogger? log = null)
    {
        _state = state;
        _module = module;
        _evaluator = evaluator ?? new JsonLogicEvaluator(log);
        _log = log ?? NullLogger.Instance;
        _priorityIndex = BuildPriorityIndex(module);
    }

    /// <summary>
    /// 建立事件優先序索引：先依 prologue.EventPriorities（最前者最優先，從 0 起算），
    /// 未列入者依 module.Events 載入順序排在 prologue 列表之後（從 prologue.Count 起算）。
    /// 結果 dict：id → 優先序整數（越小越前）。
    /// </summary>
    private static Dictionary<string, int> BuildPriorityIndex(Module module)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        // 先放 prologue.EventPriorities（保留模組作者意圖的順序）。
        var priorities = module.Prologue.EventPriorities;
        int rank = 0;
        for (int i = 0; i < priorities.Count; i++)
        {
            // 重複 id 跳過（保留首次出現的 rank）；不存在的 id 在 ModuleLoader 已驗。
            if (index.ContainsKey(priorities[i])) continue;
            index[priorities[i]] = rank++;
        }
        // 未列入的 module.Events 依 dictionary 載入序補上（IReadOnlyDictionary 的迭代序對 STJ 反序列化結果穩定）。
        foreach (var ev in module.Events.Values)
        {
            if (index.ContainsKey(ev.Id)) continue;
            index[ev.Id] = rank++;
        }
        return index;
    }

    /// <summary>UI / Bootstrap 設置 sink；命中事件後呼 sink 開對話框。</summary>
    public IEventBrokerSink? Sink { get; set; }

    /// <summary>
    /// 玩家進入指定地塊 → 掃描所有未消費 + tileEnter / flag 命中的事件；
    /// 第一張交 sink 開對話框，其餘 enqueue 至 PendingEventQueue 由現有路徑接力。
    /// </summary>
    public void OnTileEnter(string tileId)
    {
        DispatchHits(ev => MatchTileEnterOrFlag(ev, tileId));
    }

    /// <summary>
    /// 玩家執行 <paramref name="kind"/> 動作（移動 / 休息 / 觀察 / 對話 / ...）→
    /// 內部先 ActionCounts[kind]++（達標前累計），再掃描 playerAction / flag 命中事件。
    /// </summary>
    public void OnPlayerAction(PlayerActionKind kind)
    {
        _state.ActionCounts.TryGetValue(kind, out var n);
        _state.ActionCounts[kind] = n + 1;
        DispatchHits(ev => MatchPlayerActionOrFlag(ev, kind));
    }

    /// <summary>
    /// 大回合行動階段開始 → 掃描 turnAt / turnRange / flag 命中事件；
    /// turnAt 為一次性（命中即會被消費），turnRange 在區間內每回合都嘗試一次。
    /// </summary>
    public void OnActionPhaseBegin(int bigRound)
    {
        DispatchHits(ev => MatchTurnOrFlag(ev, bigRound));
    }

    // ─── 命中判斷（純函式）──────────────────────────────────────────

    private bool MatchTileEnterOrFlag(EventCard ev, string tileId) => ev.Trigger switch
    {
        TileEnterTrigger te => te.TileId == tileId,
        FlagTrigger fg      => MatchFlag(fg),
        _ => false,
    };

    private bool MatchPlayerActionOrFlag(EventCard ev, PlayerActionKind kind) => ev.Trigger switch
    {
        PlayerActionTrigger pa => pa.Kind == kind
                                  && (pa.Count is null
                                      || (_state.ActionCounts.TryGetValue(pa.Kind, out var c) && c == pa.Count.Value)),
        FlagTrigger fg => MatchFlag(fg),
        _ => false,
    };

    private bool MatchTurnOrFlag(EventCard ev, int round) => ev.Trigger switch
    {
        TurnAtTrigger ta    => ta.Round == round,
        TurnRangeTrigger tr => round >= tr.From && (tr.To is null || round <= tr.To.Value),
        FlagTrigger fg      => MatchFlag(fg),
        TurnTimerTrigger _ or ActionCountTrigger _ => LogObsoleteAndIgnore(ev),
        _ => false,
    };

    private bool MatchFlag(FlagTrigger fg)
    {
        return _state.Flags.TryGetValue(fg.Key, out var v) && JsonElementEquals(v, fg.Value);
    }

    private bool LogObsoleteAndIgnore(EventCard ev)
    {
        _log.LogWarning(
            "[EventBroker] 事件 '{EventId}' 使用 obsolete trigger '{Type}'，已忽略。請改用 turnAt / turnRange / playerAction。",
            ev.Id, ev.Trigger.GetType().Name);
        return false;
    }

    // ─── 掃描 + 派發 ────────────────────────────────────────────────

    private void DispatchHits(Func<EventCard, bool> matcher)
    {
        var hits = new List<EventCard>();
        // Phase 3 任務 14（S3）· 對候選事件先評估 RevealCondition；不通過則不算命中。
        // 構 context 一次（reveal 條件多半共享變數空間）；事件少時 O(N×M) 也可接受。
        JsonLogicContext? ctx = null;
        foreach (var ev in _module.Events.Values)
        {
            if (_state.ConsumedEventIds.Contains(ev.Id)) continue;
            if (!matcher(ev)) continue;
            if (ev.RevealCondition is not null)
            {
                ctx ??= JsonLogicContextBuilder.FromGameState(_state, _module);
                if (!_evaluator.Evaluate(ev.RevealCondition, ctx))
                {
                    _log.LogDebug("[EventBroker] 事件 '{EventId}' RevealCondition 不通過，跳過。", ev.Id);
                    continue;
                }
            }
            hits.Add(ev);
        }
        if (hits.Count == 0) return;

        // Phase 3 任務 14（S5）· 同時命中多事件 → 依 prologue.EventPriorities 排序，
        // 第一張交 sink 開對話框，其餘 enqueue 至 PendingEventQueue 由 OnEventResolved 接力。
        if (hits.Count > 1)
        {
            hits.Sort((a, b) => _priorityIndex[a.Id].CompareTo(_priorityIndex[b.Id]));
        }

        Sink?.OnEventTriggered(hits[0]);
        for (int i = 1; i < hits.Count; i++)
        {
            if (!_state.PendingEventQueue.Contains(hits[i].Id))
                _state.PendingEventQueue.Add(hits[i].Id);
        }
    }

    private static bool JsonElementEquals(System.Text.Json.JsonElement a, System.Text.Json.JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                or System.Text.Json.JsonValueKind.Null => true,
            System.Text.Json.JsonValueKind.String => a.GetString() == b.GetString(),
            System.Text.Json.JsonValueKind.Number => a.GetRawText() == b.GetRawText(),
            _ => a.GetRawText() == b.GetRawText()
        };
    }
}
