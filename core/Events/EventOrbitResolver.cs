// EventOrbitResolver — ORBIT 軌道事件解析（規格書 §1.6 / §3.6）。
//
// 與現有 Services/EventResolver 區分：
//   - Services/EventResolver  : 單一事件「擲骰 + Stat + 結果分支」結算（保持不動）
//   - Events/EventOrbitResolver : ORBIT 軌道條件評估（reveal/trigger）+ A→B→C 主迴圈
//
// 主要 API：
//   - RegisterEventToOrbit(EventInstance) — MapExpand/Action 階段事件來源呼叫
//   - EvaluateRevealCondition / EvaluateTriggerCondition — 個別條件評估
//   - ResolvePending(JsonLogicContext) → ResolutionReport — EventCheck 主迴圈
//
// 主迴圈流程（規格 §1.6）：
//   1. 開頭：所有結局卡 reveal 評估
//   2. 重新評估所有 ClassC.reveal、ClassB.trigger（升級 / 降級）
//   3. 取最前面的 ClassA 處理 → 觸發回呼（呼叫者結算 EventCard） → ChainDepth++
//   4. 連鎖深度 < 5 → 重新評估 B 與結局卡 trigger，回到 3
//   5. 連鎖深度 ≥ 5 → 把後續 A 移至下回合 queue 最前端
//   6. 結尾：再評估所有結局卡 reveal
//   7. 結局卡 ClassA 觸發 → 回報 EndingTriggered，呼叫者直接結束遊戲
//
// 條件評估失敗（變數不存在等）→ 視為 false（規格 §7.3）。
using CardNarrative.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CardNarrative.Core.Events;

/// <summary>呼叫者實作：在 A 類事件被觸發時執行（通常呼叫 Services/EventResolver.Resolve）。</summary>
public delegate void EventTriggerHandler(EventInstance instance);

public sealed class OrbitResolutionReport
{
    public List<EventInstance> Triggered { get; } = new();
    public List<EventInstance> Deferred { get; } = new();
    public EventInstance? EndingTriggered { get; set; }
    public int ChainDepthReached { get; set; }
}

public sealed class EventOrbitResolver
{
    private readonly EventOrbit _orbit;
    private readonly JsonLogicEvaluator _evaluator;
    private readonly ILogger _log;

    public EventOrbit Orbit => _orbit;

    public EventOrbitResolver(EventOrbit orbit, JsonLogicEvaluator? evaluator = null, ILogger? log = null)
    {
        _orbit = orbit;
        _evaluator = evaluator ?? new JsonLogicEvaluator(log);
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>事件來源（MapExpand / Action / 序幕）呼叫。</summary>
    public void RegisterEventToOrbit(EventInstance instance) => _orbit.Push(instance);

    /// <summary>方便建構：用 EventCard + 條件 JSON 字串建立並推入。</summary>
    public EventInstance RegisterEventToOrbit(
        EventCard card,
        string? revealConditionJson = null,
        string? triggerConditionJson = null,
        bool isEnding = false,
        int revealedTurn = 0)
    {
        var instance = new EventInstance(
            card,
            revealCondition: ParseRule(revealConditionJson),
            triggerCondition: ParseRule(triggerConditionJson),
            isEnding: isEnding,
            revealedTurn: revealedTurn);
        _orbit.Push(instance);
        return instance;
    }

    public bool EvaluateRevealCondition(EventInstance instance, JsonLogicContext ctx)
        => _evaluator.Evaluate(instance.RevealCondition, ctx);

    public bool EvaluateTriggerCondition(EventInstance instance, JsonLogicContext ctx)
        => _evaluator.Evaluate(instance.TriggerCondition, ctx);

    /// <summary>
    /// EventCheck 主迴圈。呼叫者透過 onTrigger 在 A 類事件被處理時執行
    /// （通常是 Services/EventResolver.Resolve）。回傳結算報告。
    /// </summary>
    public OrbitResolutionReport ResolvePending(JsonLogicContext ctx, EventTriggerHandler? onTrigger = null)
    {
        var report = new OrbitResolutionReport();
        _orbit.BeginEventCheck();

        // 1. 開頭：結局卡 reveal
        ReevaluateRevealAndTrigger(ctx, endingsOnly: true);

        // 2. 一般事件 reveal/trigger
        ReevaluateRevealAndTrigger(ctx, endingsOnly: false);

        // 3-5. 處理 A 類事件
        while (true)
        {
            var next = _orbit.Pending
                .Where(i => i.Class == EventOrbitClass.ClassA && !_orbit.IsTriggeredThisCheck(i.Id))
                .OrderByDescending(i => i.IsEnding) // 結局卡優先
                .FirstOrDefault();
            if (next is null) break;

            _orbit.MarkTriggered(next.Id);

            if (next.IsEnding)
            {
                // 結局卡 A 類觸發 → 直接結束遊戲（規格 §1.6）
                report.EndingTriggered = next;
                report.Triggered.Add(next);
                onTrigger?.Invoke(next);
                _orbit.Remove(next);
                break;
            }

            onTrigger?.Invoke(next);
            report.Triggered.Add(next);
            _orbit.Remove(next);

            var withinDepth = _orbit.IncrementChainDepth();
            if (!withinDepth)
            {
                // 規格 §1.6：超鏈深度 → 把剩餘 A 類事件搬到下回合 ORBIT 最前端
                var leftover = _orbit.Pending
                    .Where(i => i.Class == EventOrbitClass.ClassA && !_orbit.IsTriggeredThisCheck(i.Id))
                    .ToList();
                foreach (var l in leftover)
                {
                    _orbit.DeferToNext(l);
                    report.Deferred.Add(l);
                }
                break;
            }

            // 每處理 1 個 A 後重新評估 B 與結局卡 trigger（規格 §1.6）
            ReevaluateRevealAndTrigger(ctx, endingsOnly: false);
        }

        // 6. 結尾再評估結局卡 reveal
        ReevaluateRevealAndTrigger(ctx, endingsOnly: true);

        report.ChainDepthReached = _orbit.ChainDepth;
        return report;
    }

    /// <summary>遍歷 Pending，依 reveal/trigger 條件升降 Class。</summary>
    private void ReevaluateRevealAndTrigger(JsonLogicContext ctx, bool endingsOnly)
    {
        // 取快照避免修改集合期間迭代失敗
        var snapshot = _orbit.Pending.ToList();
        foreach (var inst in snapshot)
        {
            if (endingsOnly && !inst.IsEnding) continue;

            switch (inst.Class)
            {
                case EventOrbitClass.ClassC:
                    if (EvaluateRevealCondition(inst, ctx))
                    {
                        // reveal 通過 → 進 B；若 trigger 也通過則直接 A
                        var triggered = EvaluateTriggerCondition(inst, ctx);
                        _orbit.Promote(inst, triggered ? EventOrbitClass.ClassA : EventOrbitClass.ClassB);
                    }
                    break;
                case EventOrbitClass.ClassB:
                    if (EvaluateTriggerCondition(inst, ctx))
                        _orbit.Promote(inst, EventOrbitClass.ClassA);
                    break;
                case EventOrbitClass.ClassA:
                    // 已 A 類 → 不降級；等待結算
                    break;
            }
        }
    }

    private static System.Text.Json.Nodes.JsonNode? ParseRule(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return System.Text.Json.Nodes.JsonNode.Parse(json);
    }
}
