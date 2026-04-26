// JsonLogicContext — JsonLogic 條件評估的變數命名空間（規格書 §5.3 / §7.3）。
// 採「扁平字串路徑 → JsonNode」字典；命名遵守規格：
//   flag.<name>、hero.{hp,ap,attr.*}、companion[i].{hp,id}、companion.count、
//   turn、currentTile.{terrain,row,col,tileCardId}、orbit.{A,B,C}.count、
//   orbit.contains.<eventId>、worldFlags.<name>
//
// 為方便 JsonLogic 的 var 運算符，內部會把扁平字典轉成巢狀 JsonObject
// （用「.」分段），例：flag.warehouse_clue_a → { "flag": { "warehouse_clue_a": ... } }
//
// 此型別保持「無 GameState 依賴」，方便單元測試直接餵字典。
// BuildFromGameState 之類的高層 helper 之後可放在 EventResolver / Services 層。
using System.Text.Json.Nodes;

namespace CardNarrative.Core.Events;

public sealed class JsonLogicContext
{
    private readonly Dictionary<string, JsonNode?> _variables = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, JsonNode?> Variables => _variables;

    public JsonLogicContext Set(string path, JsonNode? value)
    {
        _variables[path] = value;
        return this;
    }

    public JsonLogicContext Set(string path, bool value) => Set(path, JsonValue.Create(value));
    public JsonLogicContext Set(string path, int value) => Set(path, JsonValue.Create(value));
    public JsonLogicContext Set(string path, double value) => Set(path, JsonValue.Create(value));
    public JsonLogicContext Set(string path, string? value) => Set(path, value is null ? null : JsonValue.Create(value));

    /// <summary>
    /// 將扁平字典展平成 JsonLogic 期望的巢狀 JsonObject，
    /// 以支援 {"var": "flag.foo"} 直接取值。
    /// </summary>
    public JsonObject ToData()
    {
        var root = new JsonObject();
        foreach (var (path, value) in _variables)
        {
            var segments = path.Split('.');
            JsonObject cursor = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                var seg = segments[i];
                if (cursor[seg] is JsonObject child)
                {
                    cursor = child;
                }
                else
                {
                    var fresh = new JsonObject();
                    cursor[seg] = fresh;
                    cursor = fresh;
                }
            }
            // Last segment holds the value. JsonNodes can only have a single parent;
            // clone via DeepClone so a JsonLogicContext can be reused.
            cursor[segments[^1]] = value?.DeepClone();
        }
        return root;
    }
}
