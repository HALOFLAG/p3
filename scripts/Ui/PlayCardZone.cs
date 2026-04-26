using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 接收拖曳手牌的中央判定區（規格書 §4.2.2）。
/// - 拖曳開始 → 中央顯示白色半透明放置區（標示「在此處釋放使用卡牌」）
/// - 滑鼠進入區內 → 高亮為金邊
/// - 區內放開 → 卡片在中央放大停留 ~0.8 秒，再飛向棄牌堆方向（右下）淡出
/// - 拖到區外放開 → 不接受，無動作
/// </summary>
public partial class PlayCardZone : Control
{
    [Signal] public delegate void CardDroppedEventHandler(string cardId);

    private const string DragKindKey = "kind";
    private const string DragKindValue = "action_card";
    private const string DragCardIdKey = "card_id";
    private const string DragCardNameKey = "card_name";
    private const string DragCardTypeKey = "card_type";
    private const string DragCardCostKey = "card_cost";

    private const int SlotWidth = 320;
    private const int SlotHeight = 220;

    private Panel? _dropSlot;
    private PackedScene? _cardViewScene;
    private bool _slotHover;

    public override void _Ready()
    {
        // Pass：drag 事件接得到，但點擊穿透給父 MainMapRenderer 處理
        MouseFilter = MouseFilterEnum.Pass;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _cardViewScene = ResourceLoader.Load<PackedScene>("res://scenes/ui/card_view.tscn");
        BuildDropSlot();
        LayoutSlot();
    }

    private void BuildDropSlot()
    {
        _dropSlot = new Panel
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _dropSlot.AddThemeStyleboxOverride("panel", BuildSlotStyle(false));
        AddChild(_dropSlot);

        var hint = new Label
        {
            Text = "在此處釋放使用卡牌",
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        hint.AddThemeFontSizeOverride("font_size", 18);
        hint.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.95f));
        hint.AddThemeColorOverride("font_outline_color", Palette.Ink);
        hint.AddThemeConstantOverride("outline_size", 4);
        _dropSlot.AddChild(hint);
        hint.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        hint.OffsetTop = 16;
        hint.OffsetBottom = 48;
    }

    private static StyleBoxFlat BuildSlotStyle(bool hover) => new()
    {
        BgColor = hover ? new Color(1, 1, 1, 0.34f) : new Color(1, 1, 1, 0.16f),
        BorderColor = hover ? Palette.Gold : new Color(1, 1, 1, 0.6f),
        BorderWidthLeft = 2, BorderWidthRight = 2,
        BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
    };

    private void LayoutSlot()
    {
        if (_dropSlot is null) return;
        var slotSize = new Vector2(SlotWidth, SlotHeight);
        _dropSlot.Position = (Size - slotSize) * 0.5f;
        _dropSlot.Size = slotSize;
    }

    private Rect2 GetSlotRect()
        => _dropSlot != null ? new Rect2(_dropSlot.Position, _dropSlot.Size) : new Rect2();

    public override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationResized:
                LayoutSlot();
                break;
            case NotificationDragBegin:
                if (_dropSlot != null && IsActionCardData(GetViewport().GuiGetDragData()))
                {
                    LayoutSlot();
                    _dropSlot.Visible = true;
                    SetSlotHover(false);
                }
                break;
            case NotificationDragEnd:
                if (_dropSlot != null) _dropSlot.Visible = false;
                SetSlotHover(false);
                break;
        }
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!IsActionCardData(data)) return false;
        var inSlot = GetSlotRect().HasPoint(atPosition);
        SetSlotHover(inSlot);
        return inSlot; // 只接受落在 slot 內
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!IsActionCardData(data)) return;
        var dict = data.As<Godot.Collections.Dictionary>();
        var cardId = dict[DragCardIdKey].AsString();
        var cardName = dict.ContainsKey(DragCardNameKey) ? dict[DragCardNameKey].AsString() : cardId;
        var cardType = dict.ContainsKey(DragCardTypeKey) ? dict[DragCardTypeKey].AsString() : "thinking";
        var cardCost = dict.ContainsKey(DragCardCostKey) ? dict[DragCardCostKey].AsInt32() : 0;

        if (_dropSlot != null) _dropSlot.Visible = false;

        SpawnPlayedCardAnimation(cardId, cardName, cardType, cardCost);
        EmitSignal(SignalName.CardDropped, cardId);
    }

    private void SetSlotHover(bool hover)
    {
        if (_dropSlot is null || _slotHover == hover) return;
        _slotHover = hover;
        _dropSlot.AddThemeStyleboxOverride("panel", BuildSlotStyle(hover));
    }

    private static bool IsActionCardData(Variant data)
    {
        var dict = data.As<Godot.Collections.Dictionary>();
        if (dict is null || !dict.ContainsKey(DragKindKey)) return false;
        return dict[DragKindKey].AsString() == DragKindValue;
    }

    /// <summary>
    /// 卡片打出視覺：
    /// - Phase 1（0~0.35s）淡入 + 從小縮放至 1.5×（Back ease 彈出感）
    /// - Phase 2（0.35~1.05s）停留中央
    /// - Phase 3（1.05~1.75s）飛向右下棄牌堆方向 + 縮小 0.25× + 淡出
    /// </summary>
    private void SpawnPlayedCardAnimation(string id, string name, string type, int cost)
    {
        if (_cardViewScene is null) return;
        var card = _cardViewScene.Instantiate<CardView>();
        AddChild(card); // _Ready 觸發
        card.SetCard(id, name, type, cost);
        card.MouseFilter = MouseFilterEnum.Ignore;

        // CardView 預設 70×104；放大時用 PivotOffset 讓中心點固定
        var cardSize = new Vector2(70, 104);
        card.PivotOffset = cardSize * 0.5f;

        // 起始：中央位置、縮 0.3×、透明
        var center = Size * 0.5f - cardSize * 0.5f;
        card.Position = center;
        card.Scale = new Vector2(0.3f, 0.3f);
        card.Modulate = new Color(1, 1, 1, 0f);

        // Phase 1: 淡入 + 放大
        var enter = CreateTween();
        enter.SetParallel(true);
        enter.TweenProperty(card, "scale", new Vector2(1.5f, 1.5f), 0.35f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        enter.TweenProperty(card, "modulate:a", 1.0f, 0.20f);

        // Phase 2 & 3: 由 timer 觸發飛走
        GetTree().CreateTimer(1.05).Timeout += () =>
        {
            if (!IsInstanceValid(card)) return;
            var exit = CreateTween();
            exit.SetParallel(true);
            // 飛向 PlayCardZone 右下方外（HandDock 棄牌堆方向）
            var endPos = new Vector2(Size.X + 80f, Size.Y + 60f);
            exit.TweenProperty(card, "position", endPos, 0.7f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
            exit.TweenProperty(card, "scale", new Vector2(0.25f, 0.25f), 0.7f);
            exit.TweenProperty(card, "modulate:a", 0.0f, 0.7f);
            exit.Finished += () =>
            {
                if (IsInstanceValid(card)) card.QueueFree();
            };
        };
    }
}
