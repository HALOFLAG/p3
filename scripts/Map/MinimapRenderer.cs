using CardNarrative.Core.Map;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Map;

/// <summary>
/// Phase 2 Task 10 — 100×100 px 俯視小地圖（規格書 §2.3 / §4.2 區塊 #13）。
///
/// 訂閱 <see cref="WorldMap"/> 三個事件 (<c>TileChanged</c> / <c>PlayerMoved</c> / <c>CameraOffsetChanged</c>) → <c>QueueRedraw</c>。
/// 自繪：地形色塊 + 黃色視野框 + 紅色玩家點 + 金色 1.5px 邊框。
/// 互動：左鍵點擊 → 發 <c>TileHighlightRequested</c> Signal；hover → tooltip 顯示地形名稱。
/// 不直接控制視野（保證主地圖視野不被點擊移動）；「回到玩家中心」按鈕由 <see cref="MainMapRenderer"/> 在 _Ready 連線。
/// </summary>
public partial class MinimapRenderer : Control
{
    public const float MinimapSizePx = 100f;
    public const float CellSize = MinimapSizePx / WorldMap.Size;
    private const float ViewportCells = 5f; // 主地圖 5×5 視野（§2.1）
    private const float BorderWidth = 1.5f;
    private const float ViewportLineWidth = 1.5f;
    private const float PlayerDotRadius = 4f;
    private const float PlayerDotOutlineWidth = 1.5f;

    private static readonly Color BorderColor = Palette.Gold;
    private static readonly Color ViewportColor = new(1.0f, 0.85f, 0.2f, 0.6f); // 黃色半透明
    private static readonly Color PlayerDotColor = new(0.78f, 0.22f, 0.22f);    // 飽和紅
    private static readonly Color PlayerDotOutline = new(1f, 1f, 1f);

    [Signal]
    public delegate void TileHighlightRequestedEventHandler(int row, int col);

    private WorldMap? _worldMap;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = ""; // 啟用 Godot tooltip 機制（_GetTooltip 才會被呼叫）
    }

    /// <summary>由 MainMapRenderer 在 _Ready 階段呼叫，注入資料源並訂閱事件。</summary>
    public void AttachWorldMap(WorldMap worldMap)
    {
        if (_worldMap == worldMap) return;
        UnsubscribeWorldMap();
        _worldMap = worldMap;
        _worldMap.TileChanged += OnTileChanged;
        _worldMap.PlayerMoved += OnPlayerMoved;
        _worldMap.CameraOffsetChanged += OnCameraOffsetChanged;
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        UnsubscribeWorldMap();
    }

    private void UnsubscribeWorldMap()
    {
        if (_worldMap is null) return;
        _worldMap.TileChanged -= OnTileChanged;
        _worldMap.PlayerMoved -= OnPlayerMoved;
        _worldMap.CameraOffsetChanged -= OnCameraOffsetChanged;
        _worldMap = null;
    }

    private void OnTileChanged(int row, int col) => QueueRedraw();
    private void OnPlayerMoved(int oldRow, int oldCol, int newRow, int newCol) => QueueRedraw();
    private void OnCameraOffsetChanged() => QueueRedraw();

    public override void _Draw()
    {
        if (_worldMap is null) return;

        // === 1. 地形色塊（每格） ===
        for (int r = 0; r < WorldMap.Size; r++)
        for (int c = 0; c < WorldMap.Size; c++)
        {
            var tile = _worldMap.GetTile(r, c);
            var rgb = MinimapPalette.GetTileDisplayColor(tile);
            if (rgb is null) continue;
            var color = Color.Color8(rgb.Value.R, rgb.Value.G, rgb.Value.B);
            var rect = new Rect2(c * CellSize, r * CellSize, CellSize, CellSize);
            DrawRect(rect, color, filled: true);
        }

        // === 2. 黃色視野框（5×5，置中於玩家位置 + 攝影機 offset） ===
        var (playerRow, playerCol) = _worldMap.PlayerPos;
        var (offsetRow, offsetCol) = _worldMap.CameraOffset;
        var centerRow = playerRow + offsetRow + 0.5f;
        var centerCol = playerCol + offsetCol + 0.5f;

        var vpX = (centerCol - ViewportCells / 2f) * CellSize;
        var vpY = (centerRow - ViewportCells / 2f) * CellSize;
        var vpW = ViewportCells * CellSize;
        var vpH = ViewportCells * CellSize;
        // Clamp 到 minimap 範圍內以免畫到邊框外
        var clampedX = Mathf.Clamp(vpX, 0f, MinimapSizePx);
        var clampedY = Mathf.Clamp(vpY, 0f, MinimapSizePx);
        var clampedRight = Mathf.Clamp(vpX + vpW, 0f, MinimapSizePx);
        var clampedBottom = Mathf.Clamp(vpY + vpH, 0f, MinimapSizePx);
        var clampedRect = new Rect2(
            clampedX, clampedY,
            Mathf.Max(0f, clampedRight - clampedX),
            Mathf.Max(0f, clampedBottom - clampedY));
        if (clampedRect.Size.X > 0 && clampedRect.Size.Y > 0)
        {
            DrawRect(clampedRect, ViewportColor, filled: false, width: ViewportLineWidth);
        }

        // === 3. 玩家紅點（白邊） ===
        var playerPx = new Vector2(
            (playerCol + 0.5f) * CellSize,
            (playerRow + 0.5f) * CellSize);
        DrawCircle(playerPx, PlayerDotRadius + PlayerDotOutlineWidth, PlayerDotOutline);
        DrawCircle(playerPx, PlayerDotRadius, PlayerDotColor);

        // === 4. 金色邊框（最後畫，蓋過視野框出界部分） ===
        DrawRect(new Rect2(0, 0, MinimapSizePx, MinimapSizePx), BorderColor, filled: false, width: BorderWidth);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_worldMap is null) return;
        if (@event is not InputEventMouseButton mb) return;
        if (!mb.Pressed || mb.ButtonIndex != MouseButton.Left) return;

        if (!TryPositionToCell(mb.Position, out var row, out var col)) return;
        EmitSignal(SignalName.TileHighlightRequested, row, col);
        AcceptEvent();
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (_worldMap is null) return "";
        if (!TryPositionToCell(atPosition, out var row, out var col)) return "";
        var tile = _worldMap.GetTile(row, col);
        if (!tile.IsPlaced) return $"({row},{col})  未放置";
        var terrainLabel = tile.IsExplored ? GetTerrainLabel(tile.Terrain) : "未探索";
        return $"({row},{col})  {terrainLabel}";
    }

    private static bool TryPositionToCell(Vector2 pos, out int row, out int col)
    {
        col = (int)(pos.X / CellSize);
        row = (int)(pos.Y / CellSize);
        if (row < 0 || row >= WorldMap.Size || col < 0 || col >= WorldMap.Size)
        {
            row = -1; col = -1;
            return false;
        }
        return true;
    }

    private static string GetTerrainLabel(MapTerrain t) => t switch
    {
        MapTerrain.Forest   => "森林",
        MapTerrain.Path     => "道路",
        MapTerrain.Grass    => "草地",
        MapTerrain.Water    => "水域",
        MapTerrain.Mountain => "山地",
        MapTerrain.Building => "建築",
        _ => "未知",
    };
}
