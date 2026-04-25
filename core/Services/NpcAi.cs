// NpcAi — 依 NPC StrategyTable（場景→偏好標籤）為玩家手牌評分，
// 挑選建議行動卡。SuggestCandidates 回 top-N 給 UI 列表。
// A2 · 新增 PlanTurn：決定同伴本回合自主執行的動作（PlayCard / Collect / Idle）。
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public sealed record NpcSuggestion(string CardId, string Reason, int Score);

public enum CompanionActionKind
{
    PlayCard,
    Collect,
    Idle
}

public sealed record CompanionAction(
    CompanionActionKind Kind,
    string? CardId = null,
    string? ResourceKey = null,
    string NarrativeHint = "");

public sealed class NpcAi
{
    public string? SuggestActionCardId(
        NpcCompanion npc,
        string scene,
        Module module,
        PlayerState player)
    {
        var list = SuggestCandidates(npc, scene, module, player);
        return list.Count > 0 ? list[0].CardId : null;
    }

    public IReadOnlyList<NpcSuggestion> SuggestCandidates(
        NpcCompanion npc,
        string scene,
        Module module,
        PlayerState player,
        int maxResults = 3)
    {
        var entry = npc.StrategyTable.FirstOrDefault(e =>
            string.Equals(e.Scene, scene, StringComparison.OrdinalIgnoreCase));
        string entryScene = entry?.Scene ?? "default";
        entry ??= npc.StrategyTable.FirstOrDefault(e =>
            string.Equals(e.Scene, "default", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return Array.Empty<NpcSuggestion>();

        var scored = new List<NpcSuggestion>();
        foreach (var cardId in player.Hand.Distinct())
        {
            if (!module.ActionCards.TryGetValue(cardId, out var card)) continue;
            var matched = card.Tags.Where(tag =>
                entry.PreferredActionTags.Any(pref =>
                    string.Equals(pref, tag, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matched.Count == 0) continue;
            string reason = $"{entryScene} 場景偏好 [{string.Join(", ", matched)}]";
            scored.Add(new NpcSuggestion(cardId, reason, matched.Count));
        }

        return scored.OrderByDescending(s => s.Score).Take(maxResults).ToList();
    }

    /// <summary>
    /// A2 · 為綁定此同伴的玩家規劃本回合自主動作。
    /// 優先序：(1) 手牌中 tag 匹配且當前可打的卡；(2) 當前 tile 未採集資源；(3) idle。
    /// 注意：同伴動作**不消耗 AP**（依使用者確認），但**沿用 UsesPerTurn/Event 冷卻**避免單卡被連打。
    /// </summary>
    public CompanionAction PlanTurn(
        NpcCompanion npc,
        string scene,
        GameState state,
        Module module,
        PlayerState boundPlayer)
    {
        // (1) 從 SuggestCandidates 挑 top-1；驗證可打才採用
        var suggestions = SuggestCandidates(npc, scene, module, boundPlayer, maxResults: 5);
        foreach (var s in suggestions)
        {
            if (!module.ActionCards.TryGetValue(s.CardId, out var card)) continue;
            if (IsCompanionPlayable(boundPlayer, state, module, card))
                return new CompanionAction(
                    CompanionActionKind.PlayCard,
                    CardId: card.Id,
                    NarrativeHint: $"{npc.Name}：{card.Name}");
        }

        // (2) 當前 tile 有未採集資源
        if (state.TileMap.TryGetValue((boundPlayer.Position.X, boundPlayer.Position.Y), out var placed)
            && module.Tiles.TryGetValue(placed.TileId, out var tile)
            && tile.Resources.Count > 0)
        {
            int playerIdx = state.Players.IndexOf(boundPlayer);
            var collected = placed.ResourcesCollectedByPlayer.TryGetValue(playerIdx, out var set)
                ? set : new HashSet<string>();
            var available = tile.Resources.FirstOrDefault(r => !collected.Contains(r.Key));
            if (available is not null)
                return new CompanionAction(
                    CompanionActionKind.Collect,
                    ResourceKey: available.Key,
                    NarrativeHint: $"{npc.Name} 協助採集 {available.Name}");
        }

        // (3) Idle 敘事
        return new CompanionAction(
            CompanionActionKind.Idle,
            NarrativeHint: $"{npc.Name} 在一旁警戒。");
    }

    /// <summary>同伴是否可代為出此卡（略過 AP 檢查；仍檢查手牌 / tile 類型 / 冷卻）。</summary>
    private static bool IsCompanionPlayable(
        PlayerState player, GameState state, Module module, ActionCard card)
    {
        if (!player.Hand.Contains(card.Id)) return false;
        if (state.TileMap.TryGetValue((player.Position.X, player.Position.Y), out var placed)
            && module.Tiles.TryGetValue(placed.TileId, out var tile)
            && tile.AllowedActionTypes.Count > 0
            && !tile.AllowedActionTypes.Contains(card.Type))
            return false;
        if (card.UsesPerTurn is int tmax
            && player.ActionCardUsesThisTurn.GetValueOrDefault(card.Id) >= tmax)
            return false;
        if (card.UsesPerEvent is int emax
            && state.ActionCardUsesThisEvent.GetValueOrDefault(card.Id) >= emax)
            return false;
        return true;
    }
}
