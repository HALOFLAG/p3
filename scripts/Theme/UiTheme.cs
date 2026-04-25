using Godot;

namespace HauntedManor.Scripts.Theme;

/// <summary>
/// 程式產生的全域 Theme 資源（規格書 §4.4 + 設計檔 Game UI Prototype.html CSS）。
/// 主場景在 _Ready 時 `Theme = UiTheme.Build()` 套用，所有子 Control 自動繼承。
/// </summary>
public static class UiTheme
{
    public static Godot.Theme Build()
    {
        var theme = new Godot.Theme();

        // ------ PanelContainer：羊皮紙背景 + 墨線邊框 ------
        var panelStyle = new StyleBoxFlat
        {
            BgColor = Palette.Paper,
            BorderColor = Palette.Ink,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        theme.SetStylebox("panel", "PanelContainer", panelStyle);

        // ------ Label：墨色字 ------
        theme.SetColor("font_color", "Label", Palette.Ink);
        theme.SetFontSize("font_size", "Label", 12);

        // ------ Button：紙色底 + 墨色邊 + hover 微亮 ------
        var btnNormal = MakeButtonStyle(Palette.PaperLight, Palette.Ink, 1);
        var btnHover = MakeButtonStyle(Palette.Paper, Palette.Ink, 1);
        var btnPressed = MakeButtonStyle(Palette.PaperDark, Palette.Ink, 1);
        var btnDisabled = MakeButtonStyle(Palette.WithAlpha(Palette.PaperLight, 0.5f), Palette.WithAlpha(Palette.Ink, 0.5f), 1);
        theme.SetStylebox("normal", "Button", btnNormal);
        theme.SetStylebox("hover", "Button", btnHover);
        theme.SetStylebox("pressed", "Button", btnPressed);
        theme.SetStylebox("disabled", "Button", btnDisabled);
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetColor("font_color", "Button", Palette.Ink);
        theme.SetColor("font_hover_color", "Button", Palette.Ink);
        theme.SetColor("font_pressed_color", "Button", Palette.Ink);
        theme.SetColor("font_disabled_color", "Button", Palette.WithAlpha(Palette.Ink, 0.5f));
        theme.SetFontSize("font_size", "Button", 12);

        // ------ ColorRect 不需要主題（直接用 .Color）------

        // ------ ProgressBar / HP bar 暫不需要全域定義（單獨組件處理） ------

        // ------ Window / AcceptDialog / ConfirmationDialog 背景 ------
        // 用 PaperLight + 金邊 1.5px，與場景主 Paper 色區別、強化視覺邊界
        var windowBorder = new StyleBoxFlat
        {
            BgColor = Palette.PaperLight,
            BorderColor = Palette.Gold,
            BorderWidthLeft = 2, BorderWidthRight = 2,
            BorderWidthTop = 2, BorderWidthBottom = 2,
            ShadowColor = Palette.WithAlpha(Palette.Ink, 0.45f),
            ShadowSize = 8,
            ShadowOffset = new Vector2(2, 4),
            ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 10, ContentMarginBottom = 14,
        };
        theme.SetStylebox("embedded_border", "Window", windowBorder);
        theme.SetStylebox("embedded_unfocused_border", "Window", windowBorder);
        theme.SetColor("title_color", "Window", Palette.Ink);
        theme.SetFontSize("title_font_size", "Window", 14);

        // AcceptDialog / ConfirmationDialog 主體 panel — 金邊 + 陰影 + PaperLight bg
        // 注意：當 Borderless = true 時，Window 的 embedded_border 與所有裝飾都不繪製，
        // 所以邊框與陰影必須加在這個 panel 上才會出現。
        var dialogPanel = new StyleBoxFlat
        {
            BgColor = Palette.PaperLight,
            BorderColor = Palette.Gold,
            BorderWidthLeft = 2, BorderWidthRight = 2,
            BorderWidthTop = 2, BorderWidthBottom = 2,
            ShadowColor = Palette.WithAlpha(Palette.Ink, 0.55f),
            ShadowSize = 12,
            ShadowOffset = new Vector2(3, 5),
            ContentMarginLeft = 18, ContentMarginRight = 18,
            ContentMarginTop = 14, ContentMarginBottom = 14,
        };
        theme.SetStylebox("panel", "AcceptDialog", dialogPanel);
        theme.SetStylebox("panel", "ConfirmationDialog", dialogPanel);
        theme.SetColor("font_color", "AcceptDialog", Palette.Ink);
        theme.SetFontSize("font_size", "AcceptDialog", 13);

        return theme;
    }

    private static StyleBoxFlat MakeButtonStyle(Color bg, Color border, int borderWidth)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthBottom = borderWidth,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
        };
    }

    /// <summary>建立 PaperDark 32px 高的 panel-header 樣式（共用）。</summary>
    public static StyleBoxFlat PanelHeaderStyle() => new()
    {
        BgColor = Palette.PaperDark,
        BorderColor = Palette.Ink,
        BorderWidthBottom = 1,
        ContentMarginLeft = 12,
        ContentMarginRight = 12,
        ContentMarginTop = 6,
        ContentMarginBottom = 6,
    };

    /// <summary>panel 主體背景樣式（淺紙色，無邊框，由父 panel 負責邊框）。</summary>
    public static StyleBoxFlat PanelBodyStyle() => new()
    {
        BgColor = Palette.PaperLight,
        ContentMarginLeft = 0,
        ContentMarginRight = 0,
        ContentMarginTop = 0,
        ContentMarginBottom = 0,
    };

    /// <summary>主要按鈕（紅底白字）— 用於 NEXT TURN、確認移動 等強調動作。</summary>
    public static StyleBoxFlat PrimaryButtonStyle(float brightness = 1.0f)
    {
        var bg = Palette.Red * brightness;
        bg.A = 1f;
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = Palette.RedDark,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 6, ContentMarginBottom = 6,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        };
    }

    /// <summary>把 Button 套成 Primary（紅底白字）。</summary>
    public static void ApplyPrimaryButtonStyle(Button btn)
    {
        btn.AddThemeStyleboxOverride("normal", PrimaryButtonStyle());
        btn.AddThemeStyleboxOverride("hover", PrimaryButtonStyle(1.10f));
        btn.AddThemeStyleboxOverride("pressed", PrimaryButtonStyle(0.85f));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        btn.AddThemeColorOverride("font_color", Palette.PaperLight);
        btn.AddThemeColorOverride("font_hover_color", Palette.PaperLight);
        btn.AddThemeColorOverride("font_pressed_color", Palette.PaperLight);
    }
}
