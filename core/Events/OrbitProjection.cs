// OrbitProjection — Phase 3 任務 14（S7）· ORBIT 提示層（純讀）。
//
// 與 EventBroker（觸發層）並行：
//   - EventBroker  : 決定「事件是否要立刻彈結算對話框」（fire-and-resolve）
//   - OrbitProjection : 重新計算 ORBIT 軌道上每張事件的 Class（C/B/A），純預覽
//
// Class 投影規則：
//   - RevealCondition 不通過        → ClassC（卡背 / 「？」）
//   - RevealCondition 通過 + Trigger 條件未滿足 → ClassB（顯示提示文字）
//   - RevealCondition 通過 + Trigger 條件滿足   → ClassA（即將觸發）
//
// 玩家視角：通常不會看到 ClassA 停留 —— broker 會立刻 fire 並從 orbit 移除事件；
// ClassA 主要用於：
//   - 條件預測（如「下一次進入 X 就會觸發」）— 寫得保守，僅當條件「現在」滿足才升 A
//   - 結局卡：條件達成後 ClassA 顯示金邊
//
// PlayerActionTrigger 不在預覽中升 A（玩家正在做動作的瞬間才命中，靜態快照視為 B）。
//
// 呼叫時機：state 變動後（玩家移動 / 結算事件 / 取得情報 / 取得裝備 / 設 flag）。
// MainBootstrap 在 broker 入口與 OnEventResolved 後呼 Refresh()。
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CardNarrative.Core.Events;

public sealed class OrbitProjection
{
    private readonly EventOrbit _orbit;
    private readonly Module _module;
    private readonly CardNarrative.Core.State.GameState _state;
    private readonly JsonLogicEvaluator _evaluator;
    private readonly ILogger _log;

    public OrbitProjection(
        EventOrbit orbit,
        Module module,
        CardNarrative.Core.State.GameState state,
        JsonLogicEvaluator? evaluator = null,
        ILogger? log = null)
    {
        _orbit = orbit;
        _module = module;
        _state = state;
        _evaluator = evaluator ?? new JsonLogicEvaluator(log);
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>
    /// 重新評估 orbit.Pending 中所有事件，依 RevealCondition + Trigger 即時狀況升降 Class。
    /// 變更時透過 EventOrbit.Promote 內部 raise OrbitChanged，UI 自動重畫。
    /// </summary>
    public void Refresh()
    {
        // 預先建一個 context（reveal 條件多半共享變數空間）。
        var ctx = JsonLogicContextBuilder.FromGameState(_state, _module, _orbit);

        foreach (var inst in _orbit.Pending.ToList())
        {
            var newClass = ProjectClass(inst, ctx);
            if (inst.Class != newClass)
                _orbit.Promote(inst, newClass);
        }
    }

    /// <summary>產生事件的提示文字（規格書 §6 模板）。轉發給 <see cref="OrbitHintTemplates.Build"/>。</summary>
    public string HintFor(EventCard card) => OrbitHintTemplates.Build(card, _module);

    // ─── 內部：Class 投影 ──────────────────────────────────────

    private EventOrbitClass ProjectClass(EventInstance inst, JsonLogicContext ctx)
    {
        // RevealCondition：null 視為「已揭露」(語意沿用 EventInstance 註解)
        bool revealed = inst.RevealCondition is null
            || _evaluator.Evaluate(inst.RevealCondition, ctx);
        if (!revealed) return EventOrbitClass.ClassC;

        return TriggerNowSatisfied(inst.Card.Trigger)
            ? EventOrbitClass.ClassA
            : EventOrbitClass.ClassB;
    }

    /// <summary>
    /// 「Trigger 條件是否在當下成立」靜態快照判斷。PlayerActionTrigger 永遠回 false
    /// （動作觸發是事件流，不是狀態，靜態快照無意義）。
    /// </summary>
    private bool TriggerNowSatisfied(EventTrigger trigger)
    {
        if (_state.Players.Count == 0) return false;
        var player = _state.CurrentPlayer;

        switch (trigger)
        {
            case TileEnterTrigger te:
                if (_state.TileMap.TryGetValue((player.Position.X, player.Position.Y), out var placed))
                    return placed.TileId == te.TileId;
                return false;
            case TurnAtTrigger ta:
                return _state.CurrentBigRound == ta.Round;
            case TurnRangeTrigger tr:
                return _state.CurrentBigRound >= tr.From
                       && (tr.To is null || _state.CurrentBigRound <= tr.To.Value);
            case PlayerActionTrigger:
                // 動作觸發無法靜態預測「現在會 fire」；保守給 ClassB（已揭露但等動作）。
                return false;
            case FlagTrigger fg:
                if (!_state.Flags.TryGetValue(fg.Key, out var v)) return false;
                return JsonElementSemanticEquals(v, fg.Value);
            default:
                return false;
        }
    }

    private static bool JsonElementSemanticEquals(System.Text.Json.JsonElement a, System.Text.Json.JsonElement b)
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
