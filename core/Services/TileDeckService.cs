// TileDeckService — 地塊牌庫抽取 + 合法放置位置判斷（Plan B 標籤規則）。
// Draw/Peek：操作 GameState.TileDeck；Place：檢查相鄰空格 + 標籤共通；
// GetValidPlacementCells：結構相鄰（不含標籤規則）；GetValidPlacementCellsForTile：含標籤規則。
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public static class TileDeckService
{
    /// <summary>Draw the top tile id from the deck (removes it). Null if empty.</summary>
    public static string? Draw(GameState state)
    {
        if (state.TileDeck.Count == 0) return null;
        var id = state.TileDeck[0];
        state.TileDeck.RemoveAt(0);
        return id;
    }

    /// <summary>Peek the top tile id without consuming it. Null if empty.</summary>
    public static string? Peek(GameState state)
        => state.TileDeck.Count == 0 ? null : state.TileDeck[0];

    /// <summary>
    /// Structural adjacency only: all empty cells orthogonally adjacent to any placed tile.
    /// Does NOT apply tag-based placement rules. Used for map bounds / legacy.
    /// Phase 2 任務 11 Stage 0：若 <see cref="GameState.GridSize"/> 已設則過濾出界格。
    /// </summary>
    public static IReadOnlyList<Position> GetValidPlacementCells(GameState state)
    {
        var seen = new HashSet<(int X, int Y)>();
        var result = new List<Position>();
        foreach (var key in state.TileMap.Keys)
        {
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var cell = (key.X + dx, key.Y + dy);
                if (state.TileMap.ContainsKey(cell)) continue;
                if (!state.IsInBounds(cell.Item1, cell.Item2)) continue;
                if (seen.Add(cell)) result.Add(new Position(cell.Item1, cell.Item2));
            }
        }
        return result;
    }

    /// <summary>
    /// Returns empty cells where <paramref name="tileId"/> can be validly placed
    /// given the full corridor + tag ruleset:
    /// <list type="bullet">
    /// <item>Tag rule (plan B): at least one adjacent tile shares a tag, or either side is neutral.</item>
    /// <item>Multi-copy adjacency: if copies of this id are already placed, the target cell must
    ///   be orthogonally adjacent to at least one of them.</item>
    /// <item>Corridor direction lock: once ≥2 copies are placed, subsequent copies must extend
    ///   the existing colinear run (no L-shaped turns).</item>
    /// <item>Single-connection headroom: applies to the <em>first</em> placement of a tile that
    ///   either (a) has <c>Copies &gt; 1</c> (multi-segment corridor needing extension room) or
    ///   (b) carries ≥2 tags (a region-bridge tile that must leave space for the new region's
    ///   tiles to attach). The candidate cell must have exactly one placed neighbor.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<Position> GetValidPlacementCells(GameState state, Module module, string tileId)
    {
        var structural = GetValidPlacementCells(state);
        var filtered = new List<Position>();
        var sameIdPositions = GetPlacedCopyPositions(state, tileId);
        bool isMultiCopy = module.Tiles.TryGetValue(tileId, out var tileDef) && tileDef.Copies > 1;
        bool isBridge = tileDef is not null && tileDef.Tags.Count >= 2;

        foreach (var cell in structural)
        {
            // 1. Same-id adjacency once copies exist
            if (sameIdPositions.Count > 0 && !IsAdjacentToSameId(state, tileId, cell))
                continue;
            // 2. Corridor direction lock (≥2 copies placed → extend the line)
            if (sameIdPositions.Count >= 2 && !IsAlongCorridorDirection(sameIdPositions, cell))
                continue;
            // 3. First-placement headroom: multi-copy corridor OR region-bridge tile
            //    (≥2 tags) must land where exactly one placed neighbor sits, so the
            //    2nd corridor segment / new-region follow-up tiles have room.
            if ((isMultiCopy || isBridge) && sameIdPositions.Count == 0
                && CountAdjacentPlaced(state, cell) != 1)
                continue;
            // 4. Tag rule (plan B)
            if (!IsTagPlacementAllowed(state, module, tileId, cell))
                continue;

            filtered.Add(cell);
        }
        return filtered;
    }

    /// <summary>True if any cell on the map is occupied by a PlacedTile with TileId == <paramref name="tileId"/>.</summary>
    public static bool HasPlacedCopy(GameState state, string tileId)
    {
        foreach (var placed in state.TileMap.Values)
            if (placed.TileId == tileId) return true;
        return false;
    }

    /// <summary>True if at least one of the 4-connected neighbors of <paramref name="pos"/> holds a PlacedTile with TileId == <paramref name="tileId"/>.</summary>
    public static bool IsAdjacentToSameId(GameState state, string tileId, Position pos)
    {
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var key = (pos.X + dx, pos.Y + dy);
            if (state.TileMap.TryGetValue(key, out var placed) && placed.TileId == tileId)
                return true;
        }
        return false;
    }

    /// <summary>Number of placed tiles (any id) orthogonally adjacent to <paramref name="pos"/>.</summary>
    public static int CountAdjacentPlaced(GameState state, Position pos)
    {
        int count = 0;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (state.TileMap.ContainsKey((pos.X + dx, pos.Y + dy))) count++;
        return count;
    }

    /// <summary>All cells currently occupied by a PlacedTile with TileId == <paramref name="tileId"/>.</summary>
    private static List<Position> GetPlacedCopyPositions(GameState state, string tileId)
    {
        var list = new List<Position>();
        foreach (var (key, placed) in state.TileMap)
            if (placed.TileId == tileId) list.Add(new Position(key.X, key.Y));
        return list;
    }

    /// <summary>
    /// Direction-lock rule for corridor extension: given ≥2 already-placed copies,
    /// the candidate must continue the colinear run in the same direction. L-shape
    /// turns are rejected.
    /// </summary>
    public static bool IsAlongCorridorDirection(IReadOnlyList<Position> placed, Position candidate)
    {
        if (placed.Count < 2) return true; // no direction yet; caller already checked adjacency

        int firstY = placed[0].Y;
        int firstX = placed[0].X;
        bool allSameY = placed.All(p => p.Y == firstY);
        bool allSameX = placed.All(p => p.X == firstX);

        if (allSameY)
        {
            // Horizontal corridor — candidate must be on the same Y, one past either end.
            int minX = int.MaxValue, maxX = int.MinValue;
            foreach (var p in placed) { if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X; }
            return candidate.Y == firstY && (candidate.X == minX - 1 || candidate.X == maxX + 1);
        }
        if (allSameX)
        {
            // Vertical corridor.
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var p in placed) { if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y; }
            return candidate.X == firstX && (candidate.Y == minY - 1 || candidate.Y == maxY + 1);
        }
        // Mixed (already L-shaped from a prior violation) — block further extension.
        return false;
    }

    /// <summary>
    /// §13-2 Plan A · 跨區域移動硬限制：
    ///   - from 或 to 任一側 Tags 為空 → 中立橋接，允許
    ///   - 否則雙方 Tags 至少共享一個字串才允許
    /// 前置：from / to 兩格皆存在 PlacedTile；呼叫端先行檢查 4-connected 與 TileMap 存在。
    /// </summary>
    public static bool IsTagMoveAllowed(GameState state, Module module, Position from, Position to)
    {
        if (!state.TileMap.TryGetValue((from.X, from.Y), out var placedFrom)) return false;
        if (!state.TileMap.TryGetValue((to.X, to.Y), out var placedTo)) return false;
        if (!module.Tiles.TryGetValue(placedFrom.TileId, out var fromTile)) return true;
        if (!module.Tiles.TryGetValue(placedTo.TileId, out var toTile)) return true;
        if (fromTile.Tags.Count == 0 || toTile.Tags.Count == 0) return true;
        foreach (var t in fromTile.Tags)
            if (toTile.Tags.Contains(t)) return true;
        return false;
    }

    /// <summary>
    /// Tag rule: at least one adjacent placed tile must satisfy one of:
    ///   - the candidate tile has empty Tags (neutral bridge)
    ///   - the neighbor has empty Tags (neutral bridge)
    ///   - neighbor's Tags and candidate's Tags share at least one tag
    /// Requires at least one adjacent placed tile to exist.
    /// </summary>
    public static bool IsTagPlacementAllowed(GameState state, Module module, string tileId, Position pos)
    {
        if (!module.Tiles.TryGetValue(tileId, out var tile)) return false;
        var candidateTags = tile.Tags;
        bool anyAdjacent = false;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var key = (pos.X + dx, pos.Y + dy);
            if (!state.TileMap.TryGetValue(key, out var placed)) continue;
            anyAdjacent = true;
            if (!module.Tiles.TryGetValue(placed.TileId, out var neighbor)) continue;
            if (candidateTags.Count == 0 || neighbor.Tags.Count == 0) return true;
            foreach (var t in candidateTags)
                if (neighbor.Tags.Contains(t)) return true;
        }
        return false && anyAdjacent; // (never reach); explicit: no neighbor satisfied
    }

    /// <summary>
    /// Place <paramref name="tileId"/> (must be top of deck) at <paramref name="pos"/>. Pops deck on success.
    /// Enforces the full rule set documented on <see cref="GetValidPlacementCells(GameState, Module, string)"/>:
    /// structural adjacency, multi-copy adjacency, corridor direction lock, first-segment headroom, tag rule.
    /// </summary>
    public static void Place(GameState state, Module module, string tileId, Position pos)
    {
        if (state.TileDeck.Count == 0 || state.TileDeck[0] != tileId)
            throw new InvalidOperationException($"tile '{tileId}' is not at the top of the deck");
        if (!state.IsInBounds(pos))
            throw new InvalidOperationException(
                $"cell ({pos.X},{pos.Y}) is outside grid bounds (size={state.GridSize})");
        if (state.TileMap.ContainsKey((pos.X, pos.Y)))
            throw new InvalidOperationException($"cell ({pos.X},{pos.Y}) already occupied");
        var structural = GetValidPlacementCells(state);
        if (!structural.Any(p => p.X == pos.X && p.Y == pos.Y))
            throw new InvalidOperationException($"cell ({pos.X},{pos.Y}) not adjacent to any placed tile");

        var sameIdPositions = GetPlacedCopyPositions(state, tileId);
        bool isMultiCopy = module.Tiles.TryGetValue(tileId, out var tileDef) && tileDef.Copies > 1;
        bool isBridge = tileDef is not null && tileDef.Tags.Count >= 2;

        if (sameIdPositions.Count > 0 && !IsAdjacentToSameId(state, tileId, pos))
            throw new InvalidOperationException(
                $"tile '{tileId}' has existing copies on the map; placement at ({pos.X},{pos.Y}) must be adjacent to one of them");
        if (sameIdPositions.Count >= 2 && !IsAlongCorridorDirection(sameIdPositions, pos))
            throw new InvalidOperationException(
                $"corridor '{tileId}' is already oriented; placement at ({pos.X},{pos.Y}) would form an L-shape (must extend the colinear run)");
        if ((isMultiCopy || isBridge) && sameIdPositions.Count == 0
            && CountAdjacentPlaced(state, pos) != 1)
        {
            var why = isMultiCopy ? "multi-copy corridor" : "region-bridge";
            throw new InvalidOperationException(
                $"{why} tile '{tileId}' must be placed where it has exactly one placed neighbor so later extensions have room; ({pos.X},{pos.Y}) has {CountAdjacentPlaced(state, pos)} neighbors");
        }
        if (!IsTagPlacementAllowed(state, module, tileId, pos))
            throw new InvalidOperationException($"tile '{tileId}' cannot be placed at ({pos.X},{pos.Y}): no adjacent tile shares a tag");

        state.TileDeck.RemoveAt(0);
        state.TileMap[(pos.X, pos.Y)] = new PlacedTile { TileId = tileId, Level = ExplorationLevel.Unknown };
    }
}
