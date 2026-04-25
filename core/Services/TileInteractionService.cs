// TileInteractionService — A6 · 可互動地圖塊的引擎服務。
// 負責：可用性檢查（Availability + cost）、扣除成本、套用 effects、記錄使用。
// 由 GameSession 轉發至此 service；UI 只需呼叫 GetAvailable / Execute。
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public static class TileInteractionService
{
    /// <summary>
    /// 取得當前玩家可執行的 interactions（過濾 availability + 成本）。
    /// 成本不足的仍列出但標記為不可執行（UI 顯示為灰暗）——此處僅檢查 availability。
    /// </summary>
    public static IReadOnlyList<TileInteraction> GetAvailable(GameState state, Module module)
    {
        var p = state.CurrentPlayer;
        if (!state.TileMap.TryGetValue((p.Position.X, p.Position.Y), out var placed))
            return Array.Empty<TileInteraction>();
        if (!module.Tiles.TryGetValue(placed.TileId, out var tile))
            return Array.Empty<TileInteraction>();
        if (tile.Interactions.Count == 0) return Array.Empty<TileInteraction>();

        int playerIdx = state.CurrentPlayerIndex;
        var result = new List<TileInteraction>();
        foreach (var it in tile.Interactions)
        {
            if (IsAvailable(it, placed, playerIdx)) result.Add(it);
        }
        return result;
    }

    /// <summary>是否可執行（availability + 成本；UI 可依此判斷按鈕啟用）。</summary>
    public static bool CanExecute(GameState state, Module module, string interactionId)
    {
        var p = state.CurrentPlayer;
        if (!state.TileMap.TryGetValue((p.Position.X, p.Position.Y), out var placed)) return false;
        if (!module.Tiles.TryGetValue(placed.TileId, out var tile)) return false;
        var it = tile.Interactions.FirstOrDefault(x => x.Id == interactionId);
        if (it is null) return false;
        int playerIdx = state.CurrentPlayerIndex;
        if (!IsAvailable(it, placed, playerIdx)) return false;
        if (!CanAfford(it.Cost, p, state)) return false;
        return true;
    }

    /// <summary>執行 interaction：扣成本、套 effects、記錄使用。成功回 true。</summary>
    public static bool Execute(GameState state, Module module, string interactionId,
                               IEffectHandler effects, ITurnLogSink? log = null)
    {
        var p = state.CurrentPlayer;
        if (!state.TileMap.TryGetValue((p.Position.X, p.Position.Y), out var placed))
            throw new InvalidOperationException("player not on any tile");
        if (!module.Tiles.TryGetValue(placed.TileId, out var tile))
            throw new InvalidOperationException($"tile '{placed.TileId}' not in module");
        var it = tile.Interactions.FirstOrDefault(x => x.Id == interactionId)
                 ?? throw new InvalidOperationException(
                     $"interaction '{interactionId}' not on tile '{tile.Id}'");

        int playerIdx = state.CurrentPlayerIndex;
        if (!IsAvailable(it, placed, playerIdx))
            throw new InvalidOperationException($"interaction '{interactionId}' not available");
        if (!CanAfford(it.Cost, p, state))
            throw new InvalidOperationException($"interaction '{interactionId}' cannot afford cost");

        // 扣成本
        if (it.Cost is { } cost)
        {
            if (cost.Resources is { } resReq)
                foreach (var (key, amount) in resReq)
                    state.Resources[key] = state.Resources.GetValueOrDefault(key) - amount;
            if (cost.EquipmentIds is { } eqReq)
                foreach (var eqId in eqReq)
                {
                    // 從玩家裝備中移除（如果有）
                    var slot = p.Equipment.FirstOrDefault(kv => kv.Value == eqId);
                    if (slot.Key != default) p.Equipment[slot.Key] = null;
                }
        }

        // 套 effects
        foreach (var e in it.Effects)
            effects.Apply(e, state, module);

        // 記錄使用
        MarkUsed(it, placed, playerIdx);

        log?.Append($"地塊互動：{tile.Name} · {it.Name}", TurnLogKind.Neutral);
        return true;
    }

    private static bool IsAvailable(TileInteraction it, PlacedTile placed, int playerIdx)
    {
        switch (it.Availability)
        {
            case TileInteractionAvailability.Unlimited:
                return true;
            case TileInteractionAvailability.OncePerVisit:
                return !(placed.InteractionsUsedThisVisit.TryGetValue(playerIdx, out var visitSet)
                         && visitSet.Contains(it.Id));
            case TileInteractionAvailability.OncePerBigRound:
                return !(placed.InteractionsUsedThisBigRound.TryGetValue(playerIdx, out var brSet)
                         && brSet.Contains(it.Id));
            case TileInteractionAvailability.OncePerGame:
                return !placed.InteractionsUsedOncePerGame.Contains(it.Id);
            default: return false;
        }
    }

    private static bool CanAfford(TileInteractionCost? cost, PlayerState p, GameState state)
    {
        if (cost is null) return true;
        if (cost.Resources is { } req)
        {
            foreach (var (key, amount) in req)
                if (state.Resources.GetValueOrDefault(key) < amount) return false;
        }
        if (cost.EquipmentIds is { } eqReq)
        {
            foreach (var eqId in eqReq)
                if (!p.Equipment.Values.Any(v => v == eqId)) return false;
        }
        return true;
    }

    private static void MarkUsed(TileInteraction it, PlacedTile placed, int playerIdx)
    {
        switch (it.Availability)
        {
            case TileInteractionAvailability.OncePerVisit:
                if (!placed.InteractionsUsedThisVisit.TryGetValue(playerIdx, out var vs))
                {
                    vs = new HashSet<string>();
                    placed.InteractionsUsedThisVisit[playerIdx] = vs;
                }
                vs.Add(it.Id);
                break;
            case TileInteractionAvailability.OncePerBigRound:
                if (!placed.InteractionsUsedThisBigRound.TryGetValue(playerIdx, out var brs))
                {
                    brs = new HashSet<string>();
                    placed.InteractionsUsedThisBigRound[playerIdx] = brs;
                }
                brs.Add(it.Id);
                break;
            case TileInteractionAvailability.OncePerGame:
                placed.InteractionsUsedOncePerGame.Add(it.Id);
                break;
        }
    }
}
