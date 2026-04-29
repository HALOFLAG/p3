// MapPathFinding — BFS 路徑規劃（規格書 §1.5 / §3.1.4）。
// Phase 3 移動 UX 改造：玩家點目標格 → 計算最短路徑 → 確認對話框顯示 AP cost。
// 規則：
//   - 四方向擴展（上 / 下 / 左 / 右），不允許對角
//   - 訪問格必須 IsPlaced=true（即 state.TileMap.ContainsKey），未放置格禁止穿越
//   - 邊界由 GameState.IsInBounds 守衛
//   - v1.12 Stage 8：相鄰格需 tag 相容（OR 邏輯）— 跨區強制走橋接 tile（如 mansion-foyer / hidden-chamber）
//   - 起點 = 玩家當前位置（必為 IsPlaced=true）；目標 = 任意已放置格
// 回傳：從 start (exclusive) 到 goal (inclusive) 的路徑步驟序列；無路 / 起點等於目標 / 目標未放置 → 空 list。
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public sealed class MapPathFinding
{
    private static readonly (int Dx, int Dy)[] Directions = { (0, 1), (0, -1), (1, 0), (-1, 0) };

    /// <summary>
    /// BFS 路徑搜尋（v1.12 Stage 8 起含 tag 相容檢查）。
    /// <paramref name="module"/> 提供 tile.Tags 資料；傳 null 跳過 tag 檢查（fallback 既有純相鄰行為）。
    /// </summary>
    public IReadOnlyList<Position> FindPath(GameState state, Position start, Position goal, Module? module = null)
    {
        // Guard：起點等於目標 → 空路徑（玩家已在該格）
        if (start.X == goal.X && start.Y == goal.Y) return Array.Empty<Position>();
        // Guard：目標必須在邊界內 + IsPlaced=true（未放置格不可走）
        if (!state.IsInBounds(goal)) return Array.Empty<Position>();
        if (!state.TileMap.ContainsKey((goal.X, goal.Y))) return Array.Empty<Position>();

        var startKey = (start.X, start.Y);
        var goalKey = (goal.X, goal.Y);
        var visited = new HashSet<(int X, int Y)> { startKey };
        var parent = new Dictionary<(int X, int Y), (int X, int Y)>();
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(startKey);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == goalKey)
            {
                // 回溯 parent chain → 路徑（不含 start，含 goal）
                var path = new List<Position>();
                var step = cur;
                while (step != startKey)
                {
                    path.Add(new Position(step.X, step.Y));
                    step = parent[step];
                }
                path.Reverse();
                return path;
            }

            foreach (var (dx, dy) in Directions)
            {
                int nx = cur.X + dx, ny = cur.Y + dy;
                var next = (nx, ny);
                if (visited.Contains(next)) continue;
                if (!state.IsInBounds(nx, ny)) continue;
                if (!state.TileMap.ContainsKey((nx, ny))) continue;
                // v1.12 Stage 8：tag 相容檢查 — 兩格 tile.tags 共享至少一個（或任一邊空）才可穿越
                if (module is not null && !AreAdjacentTilesTagCompatible(state, module, cur, next)) continue;
                visited.Add(next);
                parent[next] = cur;
                queue.Enqueue(next);
            }
        }

        // frontier 耗盡 → 不可達
        return Array.Empty<Position>();
    }

    /// <summary>
    /// v1.12 Stage 8 — 兩相鄰已放置格之 tile.tags 是否 <see cref="WorldMap.TagsCompatible"/>。
    /// </summary>
    private static bool AreAdjacentTilesTagCompatible(
        GameState state, Module module, (int X, int Y) a, (int X, int Y) b)
    {
        if (!state.TileMap.TryGetValue(a, out var pa)) return true;
        if (!state.TileMap.TryGetValue(b, out var pb)) return true;
        if (!module.Tiles.TryGetValue(pa.TileId, out var ta)) return true;
        if (!module.Tiles.TryGetValue(pb.TileId, out var tb)) return true;
        return WorldMap.TagsCompatible(ta.Tags, tb.Tags);
    }

    /// <summary>
    /// 計算給定路徑的 AP 消耗（規格 §3.1.4：本回合首格免費，第 2 格起每格 1 AP）。
    /// firstMoveAvailable=true 時 path[0] 不扣 AP；其餘每格 1 AP。
    /// </summary>
    public static int CalculateApCost(int pathLength, bool firstMoveAvailable)
    {
        if (pathLength <= 0) return 0;
        return firstMoveAvailable ? Math.Max(0, pathLength - 1) : pathLength;
    }
}
