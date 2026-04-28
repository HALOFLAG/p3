// Phase 3 移動 UX 改造 Stage B — 路徑預覽 overlay（規格書 §3.1.4 / §5.1.4）。
// 顯示：玩家 hover 已放置格時 → BFS 計算路徑 → 自繪連線 + 分段節點，提示 AP 消耗。
//
// 視覺規則：
//   - 起點圓圈（玩家所在）：綠色實心
//   - 路徑連線分段：[免費首格 / AP 內 / 超 AP] = [綠 / 綠 / 紅]
//   - 路徑節點圓環：每 step 一個圓環，顏色同對應段
//   - 紅色節點 = 該段已超出玩家當前 AP（玩家點此目標將被拒）
//
// 取得 tile 螢幕座標：MainMapRenderer 傳入 tile center 陣列（已含 push-apart offset）。
using System.Collections.Generic;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Map;

public partial class PathPreviewOverlay : Node2D
{
    private const float LineWidth = 4f;
    private const float NodeRadius = 9f;
    private const float NodeRingThickness = 3f;
    private const float PinNodeRadius = 14f;       // v1.11 終點 pin 節點放大
    private const float PinGoldRingThickness = 3f;
    private const float PinGoldRingExtra = 4f;

    private static readonly Color GreenSegment = Palette.Green;
    private static readonly Color RedSegment = Palette.Red;
    private static readonly Color PinGoldRing = Palette.Gold;

    private struct Segment
    {
        public Vector2 From;
        public Vector2 To;
        public Color Color;
    }

    private struct Node
    {
        public Vector2 Position;
        public Color Color;
        public bool IsStart;  // 起點實心；其他空心圓環
        public bool IsPin;    // v1.11 終點 pin：放大 + 金邊
    }

    private readonly List<Segment> _segments = new();
    private readonly List<Node> _nodes = new();
    private bool _hasPreview;

    public override void _Ready()
    {
        ZIndex = 100; // 高於 tile 但低於 popup
        ZAsRelative = false; // 絕對 z（不被 parent _tileLayer 的 z 干擾）
        Visible = true;
    }

    /// <summary>
    /// 設定預覽路徑。playerPos 是起點 tile center，pathCenters 是路徑各 step 的 tile center（不含 start）。
    /// 玩家 AP + firstMoveAvailable 決定每段顏色（分段邏輯：累積 AP &gt; playerAp 改紅）。
    /// </summary>
    public void SetPreview(
        Vector2 playerCenter,
        IReadOnlyList<Vector2> pathCenters,
        int playerAp,
        bool firstMoveAvailable)
    {
        _segments.Clear();
        _nodes.Clear();

        if (pathCenters.Count == 0)
        {
            _hasPreview = false;
            QueueRedraw();
            return;
        }

        // 起點：綠色實心
        _nodes.Add(new Node { Position = playerCenter, Color = GreenSegment, IsStart = true });

        Vector2 prev = playerCenter;
        int cumulativeAp = 0;
        int lastIdx = pathCenters.Count - 1;
        for (int i = 0; i < pathCenters.Count; i++)
        {
            int stepCost = (i == 0 && firstMoveAvailable) ? 0 : 1;
            cumulativeAp += stepCost;
            bool affordable = cumulativeAp <= playerAp;
            var color = affordable ? GreenSegment : RedSegment;

            _segments.Add(new Segment { From = prev, To = pathCenters[i], Color = color });
            _nodes.Add(new Node
            {
                Position = pathCenters[i],
                Color = color,
                IsStart = false,
                IsPin = (i == lastIdx),  // v1.11：終點 = pin
            });
            prev = pathCenters[i];
        }

        _hasPreview = true;
        QueueRedraw();
    }

    public void Clear()
    {
        _segments.Clear();
        _nodes.Clear();
        _hasPreview = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_hasPreview) return;

        // 連線
        foreach (var seg in _segments)
            DrawLine(seg.From, seg.To, seg.Color, LineWidth, antialiased: true);

        // 節點
        foreach (var node in _nodes)
        {
            if (node.IsStart)
            {
                // 起點實心圓 + 白色邊
                DrawCircle(node.Position, NodeRadius, node.Color);
                DrawArc(node.Position, NodeRadius, 0, Mathf.Tau, 32, Colors.White, 1.5f, antialiased: true);
            }
            else if (node.IsPin)
            {
                // v1.11 終點 pin：放大 + 金邊（強調「玩家當前選定的目標」）
                DrawCircle(node.Position, PinNodeRadius, Palette.PaperLight);
                DrawArc(node.Position, PinNodeRadius, 0, Mathf.Tau, 32, node.Color, NodeRingThickness, antialiased: true);
                DrawArc(node.Position, PinNodeRadius + PinGoldRingExtra, 0, Mathf.Tau, 32, PinGoldRing, PinGoldRingThickness, antialiased: true);
                // 紅色 pin 額外標示「不可達」（雙紅圈外加金邊）
                if (node.Color == RedSegment)
                    DrawArc(node.Position, PinNodeRadius + 8, 0, Mathf.Tau, 32, RedSegment, 2f, antialiased: true);
            }
            else
            {
                // 中間路徑節點：空心圓環
                DrawCircle(node.Position, NodeRadius, Palette.PaperLight);
                DrawArc(node.Position, NodeRadius, 0, Mathf.Tau, 32, node.Color, NodeRingThickness, antialiased: true);
                if (node.Color == RedSegment)
                    DrawArc(node.Position, NodeRadius + 4, 0, Mathf.Tau, 32, RedSegment, 2f, antialiased: true);
            }
        }
    }
}
