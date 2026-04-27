// Phase 2 任務 12 Stage 2 — 訊息氣泡 UI 元件（規格書 §1.9 / §4.2 區塊 #4）。
// 純 Control 元件、不持有 service 引用；MainBootstrap 透過 SetBubbles / Clear API 注入資料。
//   摺疊狀態：36×36 圓點 + 未讀數（含 important = 紅邊；否則金邊）
//   展開狀態：圓點右下方彈 popup（240×N），最新在上，2 行截斷，紅色高亮 important
// 點 bubble entry 發 BubbleClicked(int bubbleId) signal；MainBootstrap 反查 service 後 RequestNavigation。
using System.Collections.Generic;
using CardNarrative.Core.Models;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

public partial class MessageBubblePanel : Control
{
    [Signal] public delegate void BubbleClickedEventHandler(int bubbleId);

    private const int DotSize = 36;
    private const int PopupWidth = 240;
    private const int EntryHeight = 60;
    private const int PopupHeaderHeight = 28;
    private const int PopupGap = 8;

    private Button? _dotButton;
    private Label? _unreadLabel;
    private PanelContainer? _popup;
    private VBoxContainer? _popupList;

    private readonly List<MessageBubble> _bubbles = new();
    private bool _popupOpen;

    public override void _Ready()
    {
        // Panel 本體：固定 36×36（摺疊狀態大小）；popup 為 child overlay，不影響 Panel 尺寸
        CustomMinimumSize = new Vector2(DotSize, DotSize);
        Size = new Vector2(DotSize, DotSize);
        MouseFilter = MouseFilterEnum.Pass; // 圓點 / popup 自己吃點擊；其餘區域穿透到下方 UI

        BuildDot();
        BuildPopup();
        UpdateDotVisual();
    }

    // === 公開 API ===

    /// <summary>由 MainBootstrap 在 MessagePushed / MessageCleared 事件中呼叫，重繪圓點 + popup 內容。</summary>
    public void SetBubbles(IReadOnlyList<MessageBubble> bubbles)
    {
        _bubbles.Clear();
        _bubbles.AddRange(bubbles);
        UpdateDotVisual();
        if (_popupOpen) RebuildPopupList();
    }

    /// <summary>TurnEnd 時呼叫，清空圓點 + 關 popup。</summary>
    public void Clear()
    {
        _bubbles.Clear();
        UpdateDotVisual();
        ClosePopup();
    }

    // === 圓點 ===

    private void BuildDot()
    {
        _dotButton = new Button
        {
            Name = "DotButton",
            Position = Vector2.Zero,
            Size = new Vector2(DotSize, DotSize),
            CustomMinimumSize = new Vector2(DotSize, DotSize),
            Text = "✦",
            Flat = false,
        };
        _dotButton.AddThemeFontSizeOverride("font_size", 16);
        _dotButton.AddThemeColorOverride("font_color", Palette.Gold);
        _dotButton.Pressed += TogglePopup;
        AddChild(_dotButton);

        _unreadLabel = new Label
        {
            Name = "UnreadLabel",
            Text = "",
            Visible = false,
            Position = new Vector2(DotSize - 14, -2),
            Size = new Vector2(16, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _unreadLabel.AddThemeColorOverride("font_color", Palette.PaperLight);
        _unreadLabel.AddThemeColorOverride("font_outline_color", Palette.Ink);
        _unreadLabel.AddThemeConstantOverride("outline_size", 3);
        _unreadLabel.AddThemeFontSizeOverride("font_size", 11);
        AddChild(_unreadLabel);
    }

    private void UpdateDotVisual()
    {
        if (_dotButton is null || _unreadLabel is null) return;

        bool hasImportant = _bubbles.Exists(b => b.IsImportant);
        int count = _bubbles.Count;

        // 圓形 stylebox：黑底 + 金邊（含 important = 紅邊）
        var border = hasImportant ? Palette.RedDark : Palette.Gold;
        var dotBg = MakeCircleStyle(Palette.Ink, border, borderWidth: 2);
        var dotHover = MakeCircleStyle(Palette.InkLight, border, borderWidth: 2);
        var dotPressed = MakeCircleStyle(Palette.OrnamentInk, border, borderWidth: 2);
        _dotButton.AddThemeStyleboxOverride("normal", dotBg);
        _dotButton.AddThemeStyleboxOverride("hover", dotHover);
        _dotButton.AddThemeStyleboxOverride("pressed", dotPressed);
        _dotButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        if (count > 0)
        {
            _unreadLabel.Text = count.ToString();
            _unreadLabel.Visible = true;
        }
        else
        {
            _unreadLabel.Visible = false;
        }
    }

    private static StyleBoxFlat MakeCircleStyle(Color bg, Color border, int borderWidth)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            CornerRadiusTopLeft = DotSize,
            CornerRadiusTopRight = DotSize,
            CornerRadiusBottomLeft = DotSize,
            CornerRadiusBottomRight = DotSize,
        };
        s.BorderWidthLeft = s.BorderWidthRight = s.BorderWidthTop = s.BorderWidthBottom = borderWidth;
        return s;
    }

    // === Popup ===

    private void BuildPopup()
    {
        _popup = new PanelContainer
        {
            Name = "BubblePopup",
            Visible = false,
            // 圓點右下方彈出（圓點右側 8px、底齊圓點頂）
            Position = new Vector2(DotSize + PopupGap, 0),
            CustomMinimumSize = new Vector2(PopupWidth, PopupHeaderHeight),
            MouseFilter = MouseFilterEnum.Stop,
        };
        var popupBg = new StyleBoxFlat
        {
            BgColor = Palette.WithAlpha(Palette.Paper, 0.95f),
            BorderColor = Palette.Ink,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 4, ContentMarginBottom = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        _popup.AddThemeStyleboxOverride("panel", popupBg);
        AddChild(_popup);

        var popupVBox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        popupVBox.AddThemeConstantOverride("separation", 4);
        _popup.AddChild(popupVBox);

        // Header：標題 + 關閉鈕
        var header = new HBoxContainer { CustomMinimumSize = new Vector2(0, PopupHeaderHeight - 4) };
        var title = new Label { Text = "訊息" };
        title.AddThemeFontSizeOverride("font_size", 11);
        title.AddThemeColorOverride("font_color", Palette.OrnamentInk);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        var closeBtn = new Button { Text = "✕", CustomMinimumSize = new Vector2(20, 20), Flat = true };
        closeBtn.AddThemeFontSizeOverride("font_size", 11);
        closeBtn.Pressed += ClosePopup;
        header.AddChild(closeBtn);
        popupVBox.AddChild(header);

        // 訊息列表容器
        _popupList = new VBoxContainer
        {
            Name = "PopupList",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _popupList.AddThemeConstantOverride("separation", 3);
        popupVBox.AddChild(_popupList);
    }

    private void TogglePopup()
    {
        if (_popupOpen) ClosePopup();
        else OpenPopup();
    }

    private void OpenPopup()
    {
        if (_popup is null) return;
        RebuildPopupList();
        _popup.Visible = true;
        _popupOpen = true;
    }

    private void ClosePopup()
    {
        if (_popup is null) return;
        _popup.Visible = false;
        _popupOpen = false;
    }

    private void RebuildPopupList()
    {
        if (_popupList is null) return;
        // 清舊節點
        foreach (var child in _popupList.GetChildren())
            child.QueueFree();

        if (_bubbles.Count == 0)
        {
            var empty = new Label { Text = "（尚無訊息）", HorizontalAlignment = HorizontalAlignment.Center };
            empty.AddThemeColorOverride("font_color", Palette.OrnamentInk);
            empty.AddThemeFontSizeOverride("font_size", 11);
            empty.CustomMinimumSize = new Vector2(0, 24);
            _popupList.AddChild(empty);
            return;
        }

        // 最新在上：反序遍歷
        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            var bubble = _bubbles[i];
            _popupList.AddChild(MakeBubbleEntry(bubble));
        }
    }

    private Control MakeBubbleEntry(MessageBubble bubble)
    {
        var btn = new Button
        {
            Text = TruncateTwoLines(bubble.Text),
            CustomMinimumSize = new Vector2(PopupWidth - 12, EntryHeight),
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        btn.AddThemeFontSizeOverride("font_size", 11);

        // important = 紅邊；否則墨色邊
        var border = bubble.IsImportant ? Palette.RedDark : Palette.InkLight;
        var bg = bubble.IsImportant ? Palette.WithAlpha(Palette.Red, 0.08f) : Palette.PaperLight;
        var entryNormal = MakeEntryStyle(bg, border, 1);
        var entryHover = MakeEntryStyle(Palette.WithAlpha(border, 0.15f), border, 1);
        var entryPressed = MakeEntryStyle(Palette.WithAlpha(border, 0.25f), border, 1);
        btn.AddThemeStyleboxOverride("normal", entryNormal);
        btn.AddThemeStyleboxOverride("hover", entryHover);
        btn.AddThemeStyleboxOverride("pressed", entryPressed);
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        if (bubble.IsImportant)
            btn.AddThemeColorOverride("font_color", Palette.RedDark);

        int capturedId = bubble.Id;
        btn.Pressed += () =>
        {
            EmitSignal(SignalName.BubbleClicked, capturedId);
            ClosePopup();
        };
        return btn;
    }

    private static StyleBoxFlat MakeEntryStyle(Color bg, Color border, int borderWidth)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 4, ContentMarginBottom = 4,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        };
        s.BorderWidthLeft = s.BorderWidthRight = s.BorderWidthTop = s.BorderWidthBottom = borderWidth;
        return s;
    }

    /// <summary>規格 §1.9：文字最多 2 行截斷。Button 已開 ClipText + AutowrapMode.Word，
    /// 但仍硬截 ~36 字元（240 寬 × 2 行 / 11px font），超出加省略號。</summary>
    private static string TruncateTwoLines(string text)
    {
        const int maxChars = 36;
        if (text.Length <= maxChars) return text;
        return text.Substring(0, maxChars - 1) + "…";
    }
}
