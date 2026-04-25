using Godot;

namespace HauntedManor.Scripts.Map;

/// <summary>
/// Phase 1 Task 2 — 攝影機控制（規格書 §2.1 / §4.2.5）。
/// Task 5 起 WASD 已移除，僅保留中鍵拖曳偏移視野。
/// 玩家移動改透過點玩家格 → 玩家行動觸發器 → 移動。
/// </summary>
public partial class CameraController : Node
{
    private MainMapRenderer? _renderer;
    private bool _middleDragging;

    /// <summary>
    /// 中鍵拖曳的累積偏移（以「格」為單位，浮點）。
    /// 拖曳一格的距離由規格書 §5.1.1 BaseTileSize 推導：100 px / 格。
    /// </summary>
    private const float DragPixelsPerCell = 100f;

    public override void _Ready()
    {
        _renderer = GetParent<MainMapRenderer>();
        if (_renderer is null)
        {
            GD.PrintErr("[CameraController] 必須掛在 MainMapRenderer 之下");
        }
    }

    /// <summary>
    /// 用 _Input 而非 _UnhandledInput：Godot 的 Control 系統會在 GUI 階段
    /// 就消耗中鍵事件，使 _UnhandledInput 收不到。_Input 在 GUI 之前觸發。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (_renderer is null) return;
        var worldMap = _renderer.WorldMap;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Middle)
        {
            _middleDragging = mb.Pressed;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_middleDragging && @event is InputEventMouseMotion motion)
        {
            var (offsetRow, offsetCol) = worldMap.CameraOffset;
            offsetCol -= motion.Relative.X / DragPixelsPerCell;
            offsetRow -= motion.Relative.Y / DragPixelsPerCell;
            worldMap.SetCameraOffset(offsetRow, offsetCol);
            GetViewport().SetInputAsHandled();
        }
    }
}
