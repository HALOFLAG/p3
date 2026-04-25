// TileModifierService — 地塊修正運算（TN 修正 vs 玩家擲骰修正分工）。
// ComputeEventTn：事件 TN = card.Tn + climax + 地形/探索修正（城鎮/地下城/Mastered）。
// ComputeActionCheckModifier：行動卡擲骰加值（Unknown -1、Familiar+同地形 +1）。
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public static class TileModifierService
{
    /// <summary>
    /// Current "high-stakes" TN modifier that Special (climax) events inherit on top of their base TN.
    /// Exposed so the top bar can surface the current climax difficulty without running a full event lookup.
    /// </summary>
    public static int ComputeClimaxTn(GameState state) => state.ClimaxTnBonus;

    /// <summary>
    /// Compute the final TN for an event card at the current player's tile.
    /// Applies: base TN, climax bonus (Special events), terrain/exploration TN tweaks.
    /// </summary>
    public static int ComputeEventTn(Module module, GameState state, EventCard card)
    {
        int tn = card.Tn;
        if (card.Type == EventType.Special) tn += state.ClimaxTnBonus;

        var (terrain, level) = GetTerrainAndLevel(module, state, state.CurrentPlayer.Position);

        // Town + Negotiation → TN -1
        if (terrain == Terrain.Town && card.Type == EventType.Negotiation) tn -= 1;
        // Dungeon + Exploration → TN +1
        if (terrain == Terrain.Dungeon && card.Type == EventType.Exploration) tn += 1;
        // Mastered + Exploration event → TN -1
        if (level == ExplorationLevel.Mastered && card.Type == EventType.Exploration) tn -= 1;

        return tn;
    }

    /// <summary>
    /// Compute the modifier applied to a player's 2d6 action-card check roll
    /// based on terrain / exploration level / action type.
    /// </summary>
    public static int ComputeActionCheckModifier(Module module, GameState state, Position pos, ActionType action)
    {
        int mod = 0;
        var (terrain, level) = GetTerrainAndLevel(module, state, pos);

        // Unknown → all checks -1 to the player
        if (level == ExplorationLevel.Unknown) mod -= 1;

        // Familiar + matching-terrain action → +1 to the player
        if (level == ExplorationLevel.Familiar && TerrainMatches(terrain, action)) mod += 1;
        // Mastered implies Familiar benefits plus more (per design §8 table)
        if (level == ExplorationLevel.Mastered && TerrainMatches(terrain, action)) mod += 1;

        return mod;
    }

    private static (Terrain terrain, ExplorationLevel level) GetTerrainAndLevel(Module module, GameState state, Position pos)
    {
        if (!state.TileMap.TryGetValue((pos.X, pos.Y), out var placed))
            return (Terrain.Special, ExplorationLevel.Neutral);
        if (!module.Tiles.TryGetValue(placed.TileId, out var tile))
            return (Terrain.Special, placed.Level);
        return (tile.Terrain, placed.Level);
    }

    private static bool TerrainMatches(Terrain terrain, ActionType action) => (terrain, action) switch
    {
        (Terrain.Town, ActionType.Communication) => true,
        (Terrain.Wilderness, ActionType.Exploration) => true,
        (Terrain.Dungeon, ActionType.Combat) => true,
        _ => false
    };
}
