// Phase 2 任務 11 Stage 5 — Tile-side transformations 觸發服務（規格書 §1.5 / §3.5）。
//
// 流程：
//   1. EventCheck 階段事件 trigger 後（MainBootstrap.OnEventResolved）
//   2. 呼 MapService.TryTransformTilesForEvent(state, module, registry, eventId, ctx)
//   3. 從 registry 取該 eventId 對應的 rules（O(R)）
//   4. 對每條 rule 掃 state.TileMap 找符合 SourceTileId 的格、評估 condition
//   5. 符合則 EffectHandler.ApplyTransformTile（與 event-side outcome 走同一套機制）
//   6. 回傳變動清單（OldTileId / NewTileId / Position），讓 UI 層 emit TileTransformed
//
// 與 event-side outcome（event.outcomes.X.effects 含 TransformTileEffect）併行：
//   - 兩者皆走 EffectHandler.ApplyTransformTile，行為一致
//   - 同事件結算時，event-side 先套（outcome.Effects），再套 tile-side rules
using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Map;

public static class MapService
{
    /// <summary>
    /// 對 eventId 觸發 tile-side transformations。
    /// </summary>
    /// <returns>實際發生的變動清單；caller 用此清單 emit TileTransformed。</returns>
    public static System.Collections.Generic.IReadOnlyList<TileTransformChange> TryTransformTilesForEvent(
        GameState state,
        CardNarrative.Core.Models.Module module,
        TileTransformRegistry registry,
        string eventId,
        JsonLogicContext? ctx,
        IEffectHandler? effectHandler = null,
        JsonLogicEvaluator? evaluator = null)
    {
        var rules = registry.GetRulesForEvent(eventId);
        if (rules.Count == 0)
            return System.Array.Empty<TileTransformChange>();

        effectHandler ??= new EffectHandler();
        evaluator ??= new JsonLogicEvaluator();
        var changes = new System.Collections.Generic.List<TileTransformChange>();

        foreach (var (sourceTileId, rule) in rules)
        {
            // snapshot keys：避免 mid-iter 集合修改
            var positions = new System.Collections.Generic.List<(int X, int Y)>();
            foreach (var key in state.TileMap.Keys)
            {
                if (state.TileMap[key].TileId == sourceTileId) positions.Add(key);
            }
            foreach (var (x, y) in positions)
            {
                if (rule.Condition is not null && ctx is not null)
                {
                    if (!evaluator.Evaluate(rule.Condition, ctx)) continue;
                }
                // 走 EffectHandler 統一邏輯（重置 Level / 採集紀錄等）
                var effect = new TransformTileEffect(rule.TransformsTo, X: x, Y: y);
                effectHandler.Apply(effect, state, module);
                changes.Add(new TileTransformChange(x, y, sourceTileId, rule.TransformsTo));
            }
        }
        return changes;
    }
}

/// <summary>tile-side transformation 套用結果（caller 用來 emit TileTransformed）。</summary>
public readonly record struct TileTransformChange(
    int X,
    int Y,
    string OldTileId,
    string NewTileId);
