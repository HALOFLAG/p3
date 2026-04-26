// Phase 2 任務 11 Stage 5 — tile-side transformations 的 event-indexed 索引（規格書 §1.5）。
//
// 設計動機（v3 修訂）：原本「事件 trigger 後遍歷 9×9 tile 對每格檢查 transformations」
// 是 O(81 × R) per event；改成事件 → 規則的反向索引，事件 trigger 時 O(R) 直接查。
//
// 模組載入時掃所有 tile 的 Transformations，建：
//   Dictionary<TriggerEventId, List<(SourceTileId, TileTransformRule)>>
// 不可變字典；模組載入後 read-only → thread-safe。
//
// 用法：
//   var registry = TileTransformRegistry.Build(module);
//   var rules = registry.GetRulesForEvent("study-collapse");
//   foreach (var (sourceTileId, rule) in rules) { ... }
using CardNarrative.Core.Models;

namespace CardNarrative.Core.Map;

public sealed class TileTransformRegistry
{
    private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(string SourceTileId, TileTransformRule Rule)>> _byEventId;

    private TileTransformRegistry(
        System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(string, TileTransformRule)>> byEventId)
    {
        _byEventId = byEventId;
    }

    public static TileTransformRegistry Build(CardNarrative.Core.Models.Module module)
    {
        var dict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(string, TileTransformRule)>>();
        foreach (var tile in module.Tiles.Values)
        {
            foreach (var rule in tile.Transformations)
            {
                if (!dict.TryGetValue(rule.TriggerEventId, out var list))
                {
                    list = new System.Collections.Generic.List<(string, TileTransformRule)>();
                    dict[rule.TriggerEventId] = list;
                }
                list.Add((tile.Id, rule));
            }
        }
        return new TileTransformRegistry(dict);
    }

    /// <summary>查詢由指定 eventId 觸發的所有規則；無對應時回空 list。</summary>
    public System.Collections.Generic.IReadOnlyList<(string SourceTileId, TileTransformRule Rule)> GetRulesForEvent(string eventId)
    {
        if (_byEventId.TryGetValue(eventId, out var list)) return list;
        return System.Array.Empty<(string, TileTransformRule)>();
    }

    /// <summary>已索引的 trigger event id 清單（debug / inspection 用）。</summary>
    public System.Collections.Generic.IReadOnlyCollection<string> IndexedEventIds => _byEventId.Keys;

    public int RuleCount => _byEventId.Values.Sum(l => l.Count);
}
