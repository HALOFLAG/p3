using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 任務 9 / §4.2 區塊 #15 — ORBIT 槽位用的「事件卡」視覺。
///
/// 沿用 CardView 的 70×104 canonical 尺寸與整體風格（金邊 + 陰影 + 頂部 type band），
/// 但欄位適配 EventCard：
///   - Type band 顏色依 EventOrbitClass A/B/C
///   - 右上角徽章：IsEnding → ★（金）；否則 → EventCard.Tn 數字
///   - 中央卡名：中文 autowrap
///   - 底部：Stat 縮寫（思 / 武 / 社 / 探）
/// </summary>
public partial class OrbitCardView : PanelContainer
{
    public const int CardWidth = 70;
    public const int CardHeight = 104;

    /// <summary>玩家點擊（左鍵釋放）。父層決定要不要打開結算對話框（通常只 ClassA 才開）。</summary>
    [Signal]
    public delegate void ClickedEventHandler();

    /// <summary>當前繫結的事件實例（SetEvent 後設定，SetEmpty 後 null）。</summary>
    public EventInstance? CurrentInstance { get; private set; }

    private PanelContainer? _typeBand;
    private Label? _typeLabel;
    private Label? _badgeLabel;
    private Label? _nameLabel;
    private Label? _statLabel;

    private static readonly StyleBoxFlat EmptyStyle = new()
    {
        BgColor = Palette.WithAlpha(Palette.PaperLight, 0.4f),
        BorderColor = Palette.WithAlpha(Palette.InkLight, 0.5f),
        BorderWidthLeft = 1, BorderWidthTop = 1,
        BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
    };

    public override void _Ready()
    {
        if (_nameLabel != null) return; // idempotent

        CustomMinimumSize = new Vector2(CardWidth, CardHeight);
        AddThemeStyleboxOverride("panel", BuildCardStyle(isEnding: false));
        // 預設不收 mouse；SetEvent 啟用、SetEmpty 取消
        MouseFilter = MouseFilterEnum.Ignore;

        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.AddThemeConstantOverride("separation", 2);
        AddChild(vbox);

        // 頂部：type band（顯示 A/B/C）+ 右側徽章疊一個 HBox
        _typeBand = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 16),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _typeBand.AddThemeStyleboxOverride("panel", BuildTypeBandStyle(Palette.OrnamentInk));
        vbox.AddChild(_typeBand);

        var bandRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _typeBand.AddChild(bandRow);

        _typeLabel = MakeLabel("?", 12, Palette.PaperLight, bold: true);
        _typeLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _typeLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bandRow.AddChild(_typeLabel);

        _badgeLabel = MakeLabel("", 11, Palette.PaperLight, bold: true);
        _badgeLabel.HorizontalAlignment = HorizontalAlignment.Right;
        bandRow.AddChild(_badgeLabel);

        // 中央：卡名
        _nameLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _nameLabel.AddThemeFontSizeOverride("font_size", 11);
        _nameLabel.AddThemeColorOverride("font_color", Palette.Ink);
        vbox.AddChild(_nameLabel);

        // 底部：Stat 縮寫
        _statLabel = MakeLabel("", 9, Palette.OrnamentInk);
        _statLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_statLabel);
    }

    /// <summary>
    /// 填入事件實例（EventCard + EventOrbitClass + IsEnding）。
    /// S7：可選 <paramref name="hint"/> 參數設為 hover tooltip（OrbitProjection.HintFor 結果）；
    /// 為 null 時不顯示卡名外的額外提示。
    /// </summary>
    public void SetEvent(EventInstance instance, string? hint = null)
    {
        if (_nameLabel == null) _Ready(); // 避免在 _Ready 之前被呼叫

        CurrentInstance = instance;
        // 啟用滑鼠 hover/click（父層決定是否處理）
        MouseFilter = MouseFilterEnum.Stop;

        AddThemeStyleboxOverride("panel", BuildCardStyle(instance.IsEnding));
        // S7：tooltip 顯示提示文字（trigger 模板 + reveal 摘要）。
        TooltipText = hint ?? string.Empty;

        if (_typeBand != null)
        {
            _typeBand.AddThemeStyleboxOverride("panel", BuildTypeBandStyle(BandColorFor(instance.Class)));
        }
        if (_typeLabel != null) _typeLabel.Text = ClassLetter(instance.Class);
        if (_badgeLabel != null)
        {
            _badgeLabel.Text = instance.IsEnding ? "★" : instance.Card.Tn.ToString();
            _badgeLabel.AddThemeColorOverride("font_color", instance.IsEnding ? Palette.Gold : Palette.PaperLight);
        }
        if (_nameLabel != null) _nameLabel.Text = instance.Card.Name;
        if (_statLabel != null) _statLabel.Text = StatGlyph(instance.Card.Stat);
        Visible = true;
    }

    /// <summary>清空為「空槽」樣式（半透明虛線框）。</summary>
    public void SetEmpty()
    {
        if (_nameLabel == null) _Ready();

        CurrentInstance = null;
        MouseFilter = MouseFilterEnum.Ignore;
        TooltipText = string.Empty;

        AddThemeStyleboxOverride("panel", EmptyStyle);
        if (_typeBand != null)
            _typeBand.AddThemeStyleboxOverride("panel", BuildTypeBandStyle(Palette.WithAlpha(Palette.OrnamentInk, 0f)));
        if (_typeLabel != null) _typeLabel.Text = "";
        if (_badgeLabel != null) _badgeLabel.Text = "";
        if (_nameLabel != null) _nameLabel.Text = "";
        if (_statLabel != null) _statLabel.Text = "";
    }

    /// <summary>左鍵釋放 → emit Clicked。父層 (OrbitPanel) 決定要不要處理。</summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (CurrentInstance is null) return;
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        {
            EmitSignal(SignalName.Clicked);
            AcceptEvent();
        }
    }

    private static string ClassLetter(EventOrbitClass cls) => cls switch
    {
        EventOrbitClass.ClassA => "A",
        EventOrbitClass.ClassB => "B",
        EventOrbitClass.ClassC => "C",
        _ => "?",
    };

    private static Color BandColorFor(EventOrbitClass cls) => cls switch
    {
        EventOrbitClass.ClassA => Palette.Gold,         // 亮金
        EventOrbitClass.ClassB => Palette.OrnamentInk,  // 中棕
        EventOrbitClass.ClassC => Palette.Ink,          // 深墨（卡背感）
        _ => Palette.OrnamentInk,
    };

    private static string StatGlyph(Stat s) => s switch
    {
        Stat.Power => "武",
        Stat.Social => "社",
        Stat.Skill => "技",
        Stat.Intellect => "智",
        _ => "",
    };

    private static StyleBoxFlat BuildCardStyle(bool isEnding) => new()
    {
        BgColor = isEnding ? Palette.PaperLight : Palette.Paper,
        BorderColor = Palette.Gold,
        BorderWidthLeft = isEnding ? 3 : 2,
        BorderWidthRight = isEnding ? 3 : 2,
        BorderWidthTop = isEnding ? 3 : 2,
        BorderWidthBottom = isEnding ? 3 : 2,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        ShadowColor = Palette.WithAlpha(Palette.Ink, 0.3f),
        ShadowSize = 4,
        ShadowOffset = new Vector2(2, 2),
        ContentMarginLeft = 4, ContentMarginRight = 4,
        ContentMarginTop = 0, ContentMarginBottom = 4,
    };

    private static StyleBoxFlat BuildTypeBandStyle(Color color) => new()
    {
        BgColor = color,
        ContentMarginLeft = 4, ContentMarginRight = 4,
        ContentMarginTop = 1, ContentMarginBottom = 1,
    };

    private static Label MakeLabel(string text, int size, Color color, bool bold = false)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}
