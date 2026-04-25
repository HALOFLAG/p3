using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 共用 panel-header 元件（規格書 §4.4 + 設計檔 .panel-header CSS）。
/// 32px 高、PaperDark 背景、ink-border-bottom 1px、左圖示 + 中標題 + 右 hint。
/// </summary>
public partial class PanelHeader : PanelContainer
{
    [Export] public string IconText { get; set; } = "◆";
    [Export] public Color IconColor { get; set; } = new(0.557f, 0.424f, 0.102f); // OrnamentInk gold-ish
    [Export] public string TitleText { get; set; } = "Header";
    [Export] public string HintText { get; set; } = "";

    private Label? _iconLabel;
    private Label? _titleLabel;
    private Label? _hintLabel;

    public override void _Ready()
    {
        // 套用 panel-header 樣式
        AddThemeStyleboxOverride("panel", UiTheme.PanelHeaderStyle());

        var hbox = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(hbox);

        _iconLabel = new Label
        {
            Text = IconText,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _iconLabel.AddThemeColorOverride("font_color", IconColor);
        _iconLabel.AddThemeFontSizeOverride("font_size", 12);
        hbox.AddChild(_iconLabel);

        _titleLabel = new Label
        {
            Text = TitleText,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _titleLabel.AddThemeColorOverride("font_color", Palette.Ink);
        _titleLabel.AddThemeFontSizeOverride("font_size", 13);
        hbox.AddChild(_titleLabel);

        // spacer
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        hbox.AddChild(spacer);

        _hintLabel = new Label
        {
            Text = HintText,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _hintLabel.AddThemeColorOverride("font_color", Palette.OrnamentInk);
        _hintLabel.AddThemeFontSizeOverride("font_size", 10);
        hbox.AddChild(_hintLabel);
    }

    public void SetTitle(string text)
    {
        TitleText = text;
        if (_titleLabel != null) _titleLabel.Text = text;
    }

    public void SetHint(string text)
    {
        HintText = text;
        if (_hintLabel != null) _hintLabel.Text = text;
    }
}
