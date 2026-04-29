// Phase 3 v1.12 Stage 7 — 地塊組外框圖層（規格書 §1.5 / §3.1.4）。
// 自繪金色 3px 粗邊：按 PlacedTile.GroupInstanceId 分組，每組畫共享 cells 的外圍輪廓。
// 演算法：對每個同組 cell，逐 4 邊檢查 — 若該邊的鄰格不在同組（或不存在）即畫該邊；
//        鄰格在同組則跳過該邊（內部邊）。
// 採 TileVisual.{BackLeft,BackRight,FrontRight,FrontLeft}Global 取已透視變形的梯形 4 角。
using System.Collections.Generic;
using CardNarrative.Core.Map;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Map;

public partial class TileGroupOutlineLayer : Node2D
{
    private const float OutlineWidth = 3f;
    private static readonly Color OutlineColor = Palette.Gold;

    private WorldMap? _worldMap;
    private TileVisual[,]? _tileNodes;

    /// <summary>由 MainMapRenderer 注入：state 來源 + tile 視覺節點陣列。</summary>
    public void Configure(WorldMap worldMap, TileVisual[,] tileNodes)
    {
        _worldMap = worldMap;
        _tileNodes = tileNodes;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_worldMap is null || _tileNodes is null) return;
        var state = _worldMap.BackingState;
        if (state is null) return; // standalone 模式不畫（無 GroupInstanceId 概念）

        // 第一層分組：GroupInstanceId → list of (row, col)
        var groups = new Dictionary<int, List<(int Row, int Col)>>();
        foreach (var ((x, y), placed) in state.TileMap)
        {
            if (placed.GroupInstanceId is not int gid) continue;
            if (!groups.TryGetValue(gid, out var list))
            {
                list = new List<(int Row, int Col)>();
                groups[gid] = list;
            }
            list.Add((Row: y, Col: x)); // TileMap key (X=col, Y=row)
        }

        // 對每組逐 cell 畫外圍邊
        foreach (var (gid, cells) in groups)
        {
            var cellSet = new HashSet<(int Row, int Col)>(cells);
            foreach (var (row, col) in cells)
            {
                if (!IsValidCell(row, col)) continue;
                var node = _tileNodes[row, col];
                if (node is null) continue;
                var bl = node.BackLeftGlobal;
                var br = node.BackRightGlobal;
                var fr = node.FrontRightGlobal;
                var fl = node.FrontLeftGlobal;

                // 上邊（北 / row-1）
                if (!cellSet.Contains((row - 1, col)))
                    DrawLine(bl, br, OutlineColor, OutlineWidth, antialiased: true);
                // 右邊（東 / col+1）
                if (!cellSet.Contains((row, col + 1)))
                    DrawLine(br, fr, OutlineColor, OutlineWidth, antialiased: true);
                // 下邊（南 / row+1）
                if (!cellSet.Contains((row + 1, col)))
                    DrawLine(fr, fl, OutlineColor, OutlineWidth, antialiased: true);
                // 左邊（西 / col-1）
                if (!cellSet.Contains((row, col - 1)))
                    DrawLine(fl, bl, OutlineColor, OutlineWidth, antialiased: true);
            }
        }
    }

    private bool IsValidCell(int row, int col)
    {
        if (_tileNodes is null) return false;
        return row >= 0 && row < _tileNodes.GetLength(0)
            && col >= 0 && col < _tileNodes.GetLength(1);
    }
}
