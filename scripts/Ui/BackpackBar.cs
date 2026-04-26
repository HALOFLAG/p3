using System.Collections.Generic;
using CardNarrative.Core.Models;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 背包欄（規格書 §3.4.3）：3 個背包槽，預設摺疊只顯示最上面一張 + 右側 `>` 按鈕。
/// 互動：
/// - 摺疊：只有 slot 0 可拖；點 `>` 按鈕 = 展開（向右滑出 1, 2 + unified frame）
/// - 展開：3 槽全部可拖；點 `<` 按鈕 / 右鍵 / 外部點擊 = 摺回
/// - 卡片放入背包後自動進到 index 0（最上面）
/// </summary>
public partial class BackpackBar : Control
{
    [Signal] public delegate void EquipmentDroppedEventHandler(string sourceKind, int sourceIdx, string targetKind, int targetIdx);

    public const int CardWidth = EquipmentSlotView.CardWidth;
    public const int CardHeight = EquipmentSlotView.CardHeight;
    public const int Gap = 6;
    public const int ButtonWidth = 18;
    private const double AnimSec = 0.22;

    private readonly List<EquipmentSlotView> _slots = new();
    private Panel? _frame;
    private Button? _toggleBtn;
    private bool _expanded;
    private Tween? _activeTween;
    private Control? _topOverlay;

    /// <summary>由 LeftPanel 注入：root-level overlay，展開時把 slots 1, 2 + frame reparent 過去。</summary>
    public void SetTopOverlay(Control overlay)
    {
        _topOverlay = overlay;
    }

    private static int CollapsedFrameWidth => CardWidth;
    private static int ExpandedFrameWidth => CardWidth * 3 + Gap * 2;
    private static int CollapsedButtonX => CardWidth + 2;

    public override void _Ready()
    {
        // 與其他主槽同寬，按鈕浮在右側、不撐大 grid 欄位
        CustomMinimumSize = new Vector2(CardWidth, CardHeight);
        ClipContents = false;
        MouseFilter = MouseFilterEnum.Pass;

        // unified frame（在 slots 之下；exactly slot 高度，不外擴 → 不影響 grid 行高）
        _frame = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _frame.AddThemeStyleboxOverride("panel", BuildFrameStyle(false));
        _frame.Position = Vector2.Zero;
        _frame.Size = new Vector2(CollapsedFrameWidth, CardHeight);
        AddChild(_frame);

        // 3 個背包槽（BackpackBar 是 plain Control 不是 Container，要顯式設 Size 才會 layout）
        for (int i = 0; i < 3; i++)
        {
            var slot = new EquipmentSlotView();
            AddChild(slot);
            slot.ConfigureBackpack(i);
            slot.Position = Vector2.Zero;
            slot.Size = new Vector2(CardWidth, CardHeight);
            slot.Visible = i == 0;
            slot.IsDraggable = i == 0;
            slot.Modulate = new Color(1, 1, 1, i == 0 ? 1f : 0f);
            slot.EquipmentDropped += OnSlotDropped;
            _slots.Add(slot);
        }

        // 展開/收回按鈕
        _toggleBtn = new Button
        {
            Text = ">",
            CustomMinimumSize = new Vector2(ButtonWidth, CardHeight),
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _toggleBtn.AddThemeFontSizeOverride("font_size", 14);
        _toggleBtn.AddThemeColorOverride("font_color", Palette.Ink);
        AddChild(_toggleBtn);
        _toggleBtn.Position = new Vector2(CollapsedButtonX, 0);
        _toggleBtn.Size = new Vector2(ButtonWidth, CardHeight);
        _toggleBtn.Pressed += Toggle;
    }

    public void UpdateContents(
        IReadOnlyList<string> backpack,
        IReadOnlyDictionary<string, Equipment> catalog)
    {
        bool anyContentChanged = false;
        for (int i = 0; i < _slots.Count; i++)
        {
            var prevId = _slots[i].EquippedId;
            string? newId = i < backpack.Count ? backpack[i] : null;
            Equipment? item = null;
            if (newId is not null) catalog.TryGetValue(newId, out item);
            _slots[i].SetEquipment(newId, item);
            _slots[i].Catalog = catalog;
            if (prevId != newId) anyContentChanged = true;
        }
        // 內容變動且展開中 → 自動摺回；摺疊時 slot 0 內容變則 flash
        if (anyContentChanged && _expanded) Collapse();
        if (anyContentChanged && !_expanded && _slots[0].EquippedItem != null) FlashTopSlot();
    }

    private void OnSlotDropped(string sourceKind, int sourceIdx, string targetKind, int targetIdx)
    {
        // 不論落在哪個背包槽，最後都歸到背包頂端（slot 0）
        EmitSignal(SignalName.EquipmentDropped, sourceKind, sourceIdx, "backpack", 0);
    }

    /// <summary>
    /// 全域監聽：展開時，無論點到哪裡（包含被其他 Control 吃掉的事件）都能收回。
    /// _Input 在 GUI 之前觸發，可看到所有 mouse button events。
    /// 規則：
    /// - 展開狀態才處理
    /// - 右鍵任何位置 → 收回 + 消化事件（避免 map 的取消模式同時觸發）
    /// - 左鍵點到展開內容（slots 1/2/0）→ 不收回，讓 drag 開始
    /// - 左鍵點其他位置 → 收回（不消化，讓事件繼續傳到目標）
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!_expanded) return;
        if (@event is not InputEventMouseButton mb || !mb.Pressed) return;

        if (mb.ButtonIndex == MouseButton.Right)
        {
            Collapse();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (!IsPointOnExpandedContent(mb.GlobalPosition))
            {
                Collapse(); // 不消化事件，讓點擊傳達到 map 等目標
            }
        }
    }

    private bool IsPointOnExpandedContent(Vector2 globalPos)
    {
        foreach (var slot in _slots)
        {
            if (!slot.Visible) continue;
            var rect = new Rect2(slot.GlobalPosition, slot.Size);
            if (rect.HasPoint(globalPos)) return true;
        }
        return false;
    }

    /// <summary>
    /// 展開時，將 hit-test 區域擴大到 3 個槽寬。
    /// 沒這個 override，Godot 的 GUI 輸入路徑會在 BackpackBar 自己的 rect (98×138) 外停止下探，
    /// 導致 slots 1, 2 雖然視覺存在但收不到 mouse press → drag 起不來。
    /// </summary>
    public override bool _HasPoint(Vector2 point)
    {
        var w = _expanded ? ExpandedFrameWidth : CollapsedFrameWidth;
        // 展開時還要把右邊外凸的按鈕區域算進來；摺疊時也保留按鈕點擊區
        var totalWidth = _expanded ? w : w + ButtonWidth + 4;
        return new Rect2(Vector2.Zero, new Vector2(totalWidth, CardHeight)).HasPoint(point);
    }

    private void Toggle()
    {
        if (_expanded) Collapse(); else Expand();
    }

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        ZIndex = 10;
        _activeTween?.Kill();

        // === 把 frame + slots 1, 2 reparent 到 root overlay ===
        // 展開後它們的全域位置會跑出 BackpackBar 的 rect 範圍，會被 sibling MiddleColumn
        // 蓋住或讓 hit-test 找不到。reparent 到 root-level overlay 就沒這個問題。
        var anchorGlobal = GlobalPosition; // slot 0 的 global = BackpackBar 的 global（slot 0 在 (0,0)）
        if (_topOverlay != null)
        {
            if (_frame != null && _frame.GetParent() == this)
            {
                _frame.Reparent(_topOverlay);
                _frame.Position = anchorGlobal;
                _frame.Size = new Vector2(CollapsedFrameWidth, CardHeight);
            }
            for (int i = 1; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s.GetParent() == this) s.Reparent(_topOverlay);
                s.Visible = true;
                s.IsDraggable = true;
                s.Position = anchorGlobal; // 起點 = slot 0 的 global
                s.Modulate = new Color(1, 1, 1, 0f);
            }
        }

        _activeTween = CreateTween().SetParallel();

        // Frame 拉長 + 變濃
        if (_frame != null)
        {
            _frame.AddThemeStyleboxOverride("panel", BuildFrameStyle(true));
            _activeTween.TweenProperty(_frame, "size",
                new Vector2(ExpandedFrameWidth, CardHeight), AnimSec)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }

        // Slots 1, 2 從 anchorGlobal 滑到 anchorGlobal + offset
        for (int i = 1; i < _slots.Count; i++)
        {
            var s = _slots[i];
            var targetPos = anchorGlobal + new Vector2(i * (CardWidth + Gap), 0);
            _activeTween.TweenProperty(s, "position", targetPos, AnimSec)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            _activeTween.TweenProperty(s, "modulate:a", 1.0f, AnimSec);
        }

        if (_toggleBtn != null) _toggleBtn.Visible = false;
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        _activeTween?.Kill();
        _activeTween = CreateTween().SetParallel();

        var anchorGlobal = GlobalPosition;

        // Frame 縮回到 collapsed size
        if (_frame != null)
        {
            _activeTween.TweenProperty(_frame, "size",
                new Vector2(CollapsedFrameWidth, CardHeight), AnimSec)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        }

        // Slots 1, 2 滑回 anchorGlobal
        for (int i = 1; i < _slots.Count; i++)
        {
            var s = _slots[i];
            s.IsDraggable = false;
            _activeTween.TweenProperty(s, "position", anchorGlobal, AnimSec)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
            _activeTween.TweenProperty(s, "modulate:a", 0.0f, AnimSec);
        }

        // 動畫結束：reparent 回 BackpackBar、hide、frame 邊框淡、按鈕復原
        _activeTween.Chain().TweenCallback(Callable.From(() =>
        {
            if (_frame != null && IsInstanceValid(_frame) && _frame.GetParent() == _topOverlay)
            {
                _frame.Reparent(this);
                _frame.Position = Vector2.Zero;
                _frame.Size = new Vector2(CollapsedFrameWidth, CardHeight);
                _frame.AddThemeStyleboxOverride("panel", BuildFrameStyle(false));
                MoveChild(_frame, 0); // frame 在 slot 0 之下
            }
            for (int i = 1; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (!IsInstanceValid(s)) continue;
                if (s.GetParent() == _topOverlay) s.Reparent(this);
                s.Position = Vector2.Zero;
                s.Visible = false;
            }
            if (_toggleBtn != null && IsInstanceValid(_toggleBtn))
            {
                _toggleBtn.Position = new Vector2(CollapsedButtonX, 0);
                _toggleBtn.Visible = true;
            }
            ZIndex = 0;
        }));
    }

    private void FlashTopSlot()
    {
        var s = _slots[0];
        s.Modulate = new Color(1.4f, 1.4f, 0.9f, 1f);
        var t = CreateTween();
        t.TweenProperty(s, "modulate", Colors.White, 0.35).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    /// <summary>展開狀態用 Brown 半透明強邊框（容器感）；摺疊時邊框淡。</summary>
    private static StyleBoxFlat BuildFrameStyle(bool expanded) => new()
    {
        BgColor = Palette.WithAlpha(Palette.Brown, expanded ? 0.18f : 0.0f),
        BorderColor = Palette.WithAlpha(Palette.Brown, expanded ? 0.85f : 0.35f),
        BorderWidthLeft = 1, BorderWidthRight = 1,
        BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
    };

    public EquipmentSlotView TopSlot => _slots[0];
}
