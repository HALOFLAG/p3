// OrbitHintTemplates — Phase 3 任務 14（S7）· ORBIT 提示文字模板。
//
// 把所有面向玩家的中文文字集中在此 static class，方便日後 i18n 抽出（規格書 §6 / S8 polish）。
// 兩類來源：
//   1. Trigger 模板：依 EventTrigger 多型輸出「會在什麼時候觸發」。
//   2. RevealCondition 摘要：對 JsonLogic JsonNode 做淺解析，輸出常見 pattern 的人話。
//
// 模板輸出：[Trigger 模板] + 「；」+ [reveal 子句 1]「；」[reveal 子句 2] ...
// reveal 子句最多 3 條，超出顯示「…」；不認得的 pattern 統一輸出「(隱藏條件)」。
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardNarrative.Core.Models;

namespace CardNarrative.Core.Events;

public static class OrbitHintTemplates
{
    public const int MaxRevealClauses = 3;

    /// <summary>產生事件的完整提示文字（trigger 模板 + reveal 摘要）。</summary>
    public static string Build(EventCard card, Module module)
    {
        var trigger = TriggerHint(card.Trigger, module);
        if (card.RevealCondition is null) return trigger;

        var clauses = new List<string>(MaxRevealClauses + 1);
        CollectRevealClauses(card.RevealCondition, module, clauses);
        if (clauses.Count == 0) return trigger;

        var sb = new StringBuilder(trigger);
        sb.Append('；');
        for (int i = 0; i < clauses.Count && i < MaxRevealClauses; i++)
        {
            if (i > 0) sb.Append('；');
            sb.Append(clauses[i]);
        }
        if (clauses.Count > MaxRevealClauses) sb.Append("…");
        return sb.ToString();
    }

    /// <summary>單純 trigger 模板（不含 reveal 摘要）。</summary>
    public static string TriggerHint(EventTrigger trigger, Module module) => trigger switch
    {
        TileEnterTrigger te    => $"到達『{TileName(module, te.TileId)}』時觸發",
        TurnAtTrigger ta       => $"第 {ta.Round} 回合行動階段觸發",
        TurnRangeTrigger tr    => tr.To is int upper
            ? $"第 {tr.From}–{upper} 回合可觸發"
            : $"第 {tr.From} 回合起可觸發",
        PlayerActionTrigger pa => pa.Count is int n
            ? $"進行 {n} 次{Verb(pa.Kind)}後觸發"
            : $"進行『{Verb(pa.Kind)}』時觸發",
        FlagTrigger fg         => $"當條件 {fg.Key} 達成時觸發",
#pragma warning disable CS0618
        TurnTimerTrigger _     => "（已棄用 trigger：turnTimer）",
        ActionCountTrigger _   => "（已棄用 trigger：actionCount）",
#pragma warning restore CS0618
        _ => "未知觸發條件",
    };

    /// <summary>玩家動作 verb 中文對照。</summary>
    public static string Verb(PlayerActionKind kind) => kind switch
    {
        PlayerActionKind.Move            => "移動",
        PlayerActionKind.Rest            => "休息",
        PlayerActionKind.Observe         => "觀察",
        PlayerActionKind.Talk            => "對話",
        PlayerActionKind.PlayCard        => "出牌",
        PlayerActionKind.Mulligan        => "整理手牌",
        PlayerActionKind.Focus           => "專注",
        PlayerActionKind.Scout           => "偵察",
        PlayerActionKind.CollectResource => "採集",
        PlayerActionKind.InvestClue      => "投入線索",
        PlayerActionKind.SwapEquipment   => "換裝",
        PlayerActionKind.Interact        => "互動",
        _ => kind.ToString(),
    };

    /// <summary>
    /// 對 JsonLogic 樹做淺解析，把可辨識的 pattern 翻成中文子句加入 <paramref name="clauses"/>。
    /// 認得的 pattern：
    ///   - {"and":[...]} / {"or":[...]} → 遞迴展開（and 全列、or 取「至少 1」說明）
    ///   - {"var":"tilePlaced.X"}      → 「需先放置 {tileName(X)}」
    ///   - {"var":"tilePlacedTag.T"}   → 「需先放置含『{T}』標籤的地塊」
    ///   - {"var":"hasIntel.Y"}        → 「需取得情報『{intelName(Y)}』」
    ///   - {"var":"hasEquipment.Z"}    → 「需持有『{equipName(Z)}』」
    ///   - {"var":"event.X.consumed"}  → 「需先完成『{eventName(X)}』」
    ///   - {">=":[{"var":"hero.hp"},N]} / {"<":...} 等比較 → 「HP ≥/＜ N」
    ///   - {"==":[{"var":"event.X.outcome"},"success"]} → 「『{eventName(X)}』需成功」
    /// 不認得 → 加「(隱藏條件)」（去重，最多 1 次出現）。
    /// </summary>
    public static void CollectRevealClauses(JsonNode node, Module module, List<string> clauses)
    {
        if (clauses.Count >= MaxRevealClauses) return;
        if (node is not JsonObject obj || obj.Count == 0)
        {
            AddOpaque(clauses);
            return;
        }
        // JsonLogic 規則永遠是單 key
        var (op, val) = (obj.First().Key, obj.First().Value);

        switch (op)
        {
            case "and":
                if (val is JsonArray andArr)
                {
                    foreach (var child in andArr)
                    {
                        if (child is null) continue;
                        if (clauses.Count >= MaxRevealClauses) break;
                        CollectRevealClauses(child, module, clauses);
                    }
                    return;
                }
                AddOpaque(clauses);
                return;
            case "or":
                if (val is JsonArray orArr && orArr.Count > 0)
                {
                    var sub = new List<string>();
                    foreach (var child in orArr)
                    {
                        if (child is null) continue;
                        CollectRevealClauses(child, module, sub);
                    }
                    if (sub.Count > 0)
                        clauses.Add($"以下任一：{string.Join("、", sub)}");
                    else
                        AddOpaque(clauses);
                    return;
                }
                AddOpaque(clauses);
                return;
            case "var":
                AddVarClause(val, module, clauses);
                return;
            case "==" or ">=" or "<=" or ">" or "<":
                AddCompareClause(op, val, module, clauses);
                return;
            default:
                AddOpaque(clauses);
                return;
        }
    }

    private static void AddVarClause(JsonNode? val, Module module, List<string> clauses)
    {
        var path = val?.GetValue<string>();
        if (string.IsNullOrEmpty(path)) { AddOpaque(clauses); return; }
        if (TryStripPrefix(path, "tilePlaced.", out var tileId))
            clauses.Add($"需先放置「{TileName(module, tileId)}」");
        else if (TryStripPrefix(path, "tilePlacedTag.", out var tag))
            clauses.Add($"需先放置含「{tag}」標籤的地塊");
        else if (TryStripPrefix(path, "hasIntel.", out var intelId))
            clauses.Add($"需取得情報「{IntelName(module, intelId)}」");
        else if (TryStripPrefix(path, "hasEquipment.", out var equipId))
            clauses.Add($"需持有「{EquipName(module, equipId)}」");
        else if (TryStripEventConsumed(path, out var eid))
            clauses.Add($"需先完成「{EventName(module, eid)}」");
        else
            AddOpaque(clauses);
    }

    private static void AddCompareClause(string op, JsonNode? val, Module module, List<string> clauses)
    {
        if (val is not JsonArray arr || arr.Count != 2) { AddOpaque(clauses); return; }
        var lhs = arr[0]; var rhs = arr[1];
        // 形如 {">=":[{"var":"hero.hp"}, 5]}
        if (lhs is JsonObject lhsObj && lhsObj.ContainsKey("var")
            && lhsObj["var"]?.GetValue<string>() is string lhsPath)
        {
            // event.X.outcome == "success"
            if (op == "==" && TryStripEventOutcome(lhsPath, out var eid)
                && rhs is not null && rhs.GetValueKind() == JsonValueKind.String)
            {
                var tier = rhs.GetValue<string>();
                var tierLabel = tier switch
                {
                    "success" => "需成功",
                    "partialSuccess" => "需部分成功",
                    "failure" => "需失敗",
                    _ => $"結果為「{tier}」"
                };
                clauses.Add($"「{EventName(module, eid)}」{tierLabel}");
                return;
            }
            // hero.hp / hero.hpRatio / turn / hero.attr.* >= N
            if (rhs is not null && (rhs.GetValueKind() == JsonValueKind.Number))
            {
                var n = rhs.GetValue<double>();
                var lhsLabel = HumanLhs(lhsPath);
                clauses.Add($"{lhsLabel} {op} {(n == (long)n ? ((long)n).ToString() : n.ToString("0.##"))}");
                return;
            }
        }
        AddOpaque(clauses);
    }

    private static string HumanLhs(string path) => path switch
    {
        "hero.hp"        => "HP",
        "hero.hpMax"     => "HP 上限",
        "hero.hpRatio"   => "HP 比例",
        "hero.ap"        => "AP",
        "turn"           => "回合數",
        "hero.attr.power"     => "武",
        "hero.attr.social"    => "社",
        "hero.attr.skill"     => "技",
        "hero.attr.intellect" => "智",
        _ => path,
    };

    private static bool TryStripPrefix(string path, string prefix, out string rest)
    {
        if (path.StartsWith(prefix, StringComparison.Ordinal))
        {
            rest = path.Substring(prefix.Length);
            return true;
        }
        rest = string.Empty;
        return false;
    }

    /// <summary>解析 "event.&lt;id&gt;.consumed" 取出 id。</summary>
    private static bool TryStripEventConsumed(string path, out string eventId)
    {
        eventId = string.Empty;
        if (!path.StartsWith("event.", StringComparison.Ordinal)) return false;
        if (!path.EndsWith(".consumed", StringComparison.Ordinal)) return false;
        eventId = path.Substring("event.".Length, path.Length - "event.".Length - ".consumed".Length);
        return eventId.Length > 0;
    }

    /// <summary>解析 "event.&lt;id&gt;.outcome" 取出 id。</summary>
    private static bool TryStripEventOutcome(string path, out string eventId)
    {
        eventId = string.Empty;
        if (!path.StartsWith("event.", StringComparison.Ordinal)) return false;
        if (!path.EndsWith(".outcome", StringComparison.Ordinal)) return false;
        eventId = path.Substring("event.".Length, path.Length - "event.".Length - ".outcome".Length);
        return eventId.Length > 0;
    }

    private static void AddOpaque(List<string> clauses)
    {
        const string opaque = "(隱藏條件)";
        if (clauses.Count == 0 || clauses[^1] != opaque)
            clauses.Add(opaque);
    }

    private static string TileName(Module module, string id) =>
        module.Tiles.TryGetValue(id, out var t) ? t.Name : id;

    private static string EventName(Module module, string id) =>
        module.Events.TryGetValue(id, out var e) ? e.Name : id;

    private static string IntelName(Module module, string id) =>
        module.Intel.TryGetValue(id, out var i) ? i.Name : id;

    private static string EquipName(Module module, string id) =>
        module.Equipment.TryGetValue(id, out var e) ? e.Name : id;
}
