// TileProgressService — 地塊探索等級推進（四路徑：首次進入、行動卡、事件結果、線索投資）。
// 每地塊每大回合最多推進 +2；重要地塊封頂 Familiar；行動卡每訪次僅一次；
// 線索投資扣 2 枚 clue_shard 換 +1。ResetBigRound 於新大回合開頭清零。
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public static class TileProgressService
{
    public const int BigRoundProgressCap = 2;
    public const string ClueResourceKey = "clue_shard";
    public const int ClueInvestmentCost = 2;

    /// <summary>
    /// Apply a progress delta to the tile at `pos`, respecting:
    /// - same-big-round cumulative cap of +2 per tile
    /// - Important tiles capped at Familiar (+1)
    /// - ActionCard per-visit one-shot rule
    /// Returns the actual delta applied (may be 0 if capped/blocked).
    /// </summary>
    public static int Advance(GameState state, Module module, Position pos, int requestedDelta, ProgressReason reason)
    {
        if (!state.TileMap.TryGetValue((pos.X, pos.Y), out var placed)) return 0;

        // Per-visit one-shot for ActionCard
        if (reason == ProgressReason.ActionCard)
        {
            if (placed.ActionCardProgressUsedThisVisit) return 0;
            if (requestedDelta > 1) requestedDelta = 1;
        }

        // Reset big-round counter if we rolled to a newer round
        if (placed.LastProgressBigRound != state.CurrentBigRound)
        {
            placed.ProgressGainedThisBigRound = 0;
            placed.LastProgressBigRound = state.CurrentBigRound;
        }

        // Compute cap for positive deltas
        int delta = requestedDelta;
        if (delta > 0)
        {
            int remainingInRound = BigRoundProgressCap - placed.ProgressGainedThisBigRound;
            if (remainingInRound <= 0) return 0;
            if (delta > remainingInRound) delta = remainingInRound;

            // Important-tile ceiling (Familiar = +1)
            bool important = module.Tiles.TryGetValue(placed.TileId, out var tile) && tile.Important;
            int target = (int)placed.Level + delta;
            int ceiling = important ? (int)ExplorationLevel.Familiar : (int)ExplorationLevel.Mastered;
            if (target > ceiling) target = ceiling;
            delta = target - (int)placed.Level;
            if (delta <= 0) return 0;
        }
        else if (delta < 0)
        {
            int target = (int)placed.Level + delta;
            int floor = (int)ExplorationLevel.Unknown;
            if (target < floor) target = floor;
            delta = target - (int)placed.Level;
            if (delta >= 0) return 0;
        }
        else
        {
            return 0;
        }

        placed.Level = (ExplorationLevel)((int)placed.Level + delta);
        if (delta > 0) placed.ProgressGainedThisBigRound += delta;
        if (reason == ProgressReason.ActionCard) placed.ActionCardProgressUsedThisVisit = true;
        return delta;
    }

    /// <summary>
    /// Promote Unknown → Unfamiliar on first entry. Idempotent for already-entered tiles.
    /// </summary>
    public static void PromoteOnFirstEnter(PlacedTile placed)
    {
        if (placed.Level == ExplorationLevel.Unknown)
            placed.Level = ExplorationLevel.Unfamiliar;
    }

    /// <summary>
    /// Clear per-big-round counters on every tile. Called at the start of a new big round.
    /// </summary>
    public static void ResetBigRound(GameState state)
    {
        foreach (var placed in state.TileMap.Values)
        {
            placed.ProgressGainedThisBigRound = 0;
            placed.LastProgressBigRound = state.CurrentBigRound;
            // A6 · Interaction 每大回合冷卻重置
            foreach (var perRound in placed.InteractionsUsedThisBigRound.Values)
                perRound.Clear();
        }
    }

    /// <summary>
    /// Spend 2 clue shards to gain +1 progress (ClueInvestment reason).
    /// Returns true if successful.
    /// </summary>
    public static bool TryInvestClues(GameState state, Module module, Position pos)
    {
        int clues = state.Resources.GetValueOrDefault(ClueResourceKey);
        if (clues < ClueInvestmentCost) return false;
        int applied = Advance(state, module, pos, 1, ProgressReason.ClueInvestment);
        if (applied <= 0) return false;
        state.Resources[ClueResourceKey] = clues - ClueInvestmentCost;
        return true;
    }
}
