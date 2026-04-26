// Phase 2 任務 11 Stage 5 — Tile-side transformation rule（規格書 §1.5）。
//
// 嵌在 tile.json 內的 transformations 陣列；EventCheck 階段事件 trigger 後，
// MapService 從 TileTransformRegistry 索引該 eventId，對符合的 tile 評估 condition
// 並套用 TransformTileEffect。
//
// 兩條 transformTile 路徑：
//   - Event-side（已在 Stage 3.5）：event.outcomes.X.effects 含 TransformTileEffect
//   - Tile-side（本 stage）：tile 自身宣告「在 event Y trigger 時變成 Z」
//
// 兩者皆走 EffectHandler.ApplyTransformTile（同一機制），確保行為一致。
using System.Text.Json.Nodes;

namespace CardNarrative.Core.Models;

public sealed record TileTransformRule(
    string TriggerEventId,
    string TransformsTo)
{
    /// <summary>
    /// 選用 JsonLogic 條件（規格書 §7.3）。null 視為「無條件成立」。
    /// 條件評估走 JsonLogicEvaluator + JsonLogicContextBuilder.FromGameState 提供的命名空間。
    /// </summary>
    public JsonNode? Condition { get; init; }
}
