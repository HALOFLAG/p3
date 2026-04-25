using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 區塊 #16 — HandDock 手牌區（中欄底部，~864×120）。
/// 本次為 placeholder：5 張示範手牌 + 右側牌堆指示。
/// </summary>
public partial class HandDock : PanelContainer
{
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.PaperDark,
            BorderColor = Palette.Ink,
            BorderWidthTop = 1,
            ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 8, ContentMarginBottom = 8,
        });

        var hbox = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        hbox.AddThemeConstantOverride("separation", 8);
        AddChild(hbox);

        // 5 張手牌 placeholder（卡型示意）
        string[] cardNames = { "交叉考據", "威嚇", "追蹤足跡", "靈感推演", "巧言令色" };
        string[] cardTypes = { "知識", "戰鬥", "探索", "知識", "社交" };
        Color[] cardColors = { Palette.Purple, Palette.Red, Palette.Green, Palette.Purple, Palette.Blue };

        for (int i = 0; i < cardNames.Length; i++)
        {
            hbox.AddChild(MakeHandCard(cardNames[i], cardTypes[i], cardColors[i]));
        }

        // spacer
        hbox.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        // 牌堆指示（右側）
        var deckIndicator = new PanelContainer { CustomMinimumSize = new Vector2(70, 96) };
        deckIndicator.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.WithAlpha(Palette.InkLight, 0.6f),
            BorderColor = Palette.Ink,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        var di = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        deckIndicator.AddChild(di);
        var top = MakeColoredLabel("牌堆", 11, Palette.PaperLight);
        top.HorizontalAlignment = HorizontalAlignment.Center;
        di.AddChild(top);
        var num = MakeColoredLabel("12", 22, Palette.Gold);
        num.HorizontalAlignment = HorizontalAlignment.Center;
        di.AddChild(num);
        hbox.AddChild(deckIndicator);
    }

    private static PanelContainer MakeHandCard(string name, string type, Color accent)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(70, 96) };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Paper,
            BorderColor = Palette.Gold,
            BorderWidthLeft = 2, BorderWidthRight = 2,
            BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ShadowColor = Palette.WithAlpha(Palette.Ink, 0.3f),
            ShadowSize = 4,
            ShadowOffset = new Vector2(2, 2),
        });
        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        v.AddThemeConstantOverride("separation", 4);
        card.AddChild(v);

        // 卡片頂部色帶
        var typeBand = new PanelContainer { CustomMinimumSize = new Vector2(0, 16) };
        typeBand.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = accent,
            ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 1, ContentMarginBottom = 1,
        });
        var tb = MakeColoredLabel(type, 9, Palette.PaperLight);
        tb.HorizontalAlignment = HorizontalAlignment.Center;
        typeBand.AddChild(tb);
        v.AddChild(typeBand);

        var nameLabel = MakeColoredLabel(name, 10, Palette.Ink);
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(nameLabel);

        return card;
    }

    private static Label MakeColoredLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}
