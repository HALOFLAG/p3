// JsonLogicEvaluator — JsonLogic 條件評估器（規格書 §5.3 / §7.3）。
// 包裝 json-everything 的 JsonLogic 套件，提供 Evaluate(rule, ctx) → bool；
// 任何錯誤（變數缺、型別錯、null rule）皆回傳 false 並透過 ILogger 記錄 warning。
//
// 設計原則（規格 §7.3）：
//   - 條件評估失敗（變數不存在等）→ false，不丟例外
//   - 規則為 null → 視為「無條件成立」(true)，方便事件 reveal/trigger 缺省
//   - 純資料、可重入；context 可重用
//
// 支援的運算符全部走 JsonLogic 預設（==, !=, <, <=, >, >=, and, or, !, +, -, *, /,
// %, in, if, var, cat, missing, missing_some, etc.）—— 不另外註冊。
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CardNarrative.Core.Events;

public sealed class JsonLogicEvaluator
{
    private readonly ILogger _log;

    public JsonLogicEvaluator(ILogger? log = null)
    {
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>
    /// 評估 JsonLogic 規則。null 規則回傳 true（缺省「無條件成立」）。
    /// 任何 JsonLogic 錯誤回傳 false 並 log warning。
    /// </summary>
    public bool Evaluate(JsonNode? rule, JsonLogicContext context)
    {
        if (rule is null) return true;
        try
        {
            var data = context.ToData();
            var parsed = JsonSerializer.Deserialize<Rule>(rule.ToJsonString());
            if (parsed is null)
            {
                _log.LogWarning("JsonLogic: rule failed to parse: {Rule}", rule.ToJsonString());
                return false;
            }
            var result = parsed.Apply(data);
            return Coerce(result);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "JsonLogic: evaluation error for rule {Rule}", rule.ToJsonString());
            return false;
        }
    }

    /// <summary>JsonLogic 的「真值」語意：null/0/""/[] → false，其餘為 true。</summary>
    private static bool Coerce(JsonNode? n)
    {
        if (n is null) return false;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b)) return b;
            if (v.TryGetValue<int>(out var i)) return i != 0;
            if (v.TryGetValue<long>(out var l)) return l != 0;
            if (v.TryGetValue<double>(out var d)) return d != 0d;
            if (v.TryGetValue<string>(out var s)) return !string.IsNullOrEmpty(s);
            return false;
        }
        if (n is JsonArray arr) return arr.Count > 0;
        if (n is JsonObject obj) return obj.Count > 0;
        return false;
    }
}
