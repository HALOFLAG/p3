using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 區塊 #7 + #8 — LeftPanel（依用戶版面，垂直 3 區）。
/// 1) 主角區：HP 條 + 4 屬性方塊（紅綠藍紫）+ 主角技能 stub
/// 2) 夥伴區：1~3 個夥伴 HP + 技能 stub
/// 3) 裝備卡 3×3 九宮格 placeholder
/// </summary>
public partial class LeftPanel : PanelContainer
{
    private ProgressBar? _heroHpBar;
    private Label? _heroHpLabel;

    public override void _Ready()
    {
        // 套用 PaperLight 背景 + ink-border-right
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.PaperLight,
            BorderColor = Palette.Ink,
            BorderWidthRight = 1,
            ContentMarginLeft = 0, ContentMarginRight = 0,
            ContentMarginTop = 0, ContentMarginBottom = 0,
        });

        var vbox = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        vbox.AddThemeConstantOverride("separation", 0);
        AddChild(vbox);

        // === 1. 主角區 ===
        AddPanelHeader(vbox, "◆", Palette.Gold, "主角", "奧斯卡偵探");
        var heroBox = MakeContentBox();
        vbox.AddChild(heroBox);
        BuildHeroBlock(heroBox);

        // === 2. 夥伴區 ===
        AddPanelHeader(vbox, "○", Palette.Blue, "夥伴", "0/3");
        var companionBox = MakeContentBox();
        vbox.AddChild(companionBox);
        BuildCompanionPlaceholder(companionBox);

        // === 3. 裝備卡 3×3 九宮格 ===
        AddPanelHeader(vbox, "✦", Palette.Gold, "裝備", "3×3");
        var equipBox = MakeContentBox();
        vbox.AddChild(equipBox);
        BuildEquipmentGrid(equipBox);

        // 填充剩餘空間
        vbox.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
    }

    private static MarginContainer MakeContentBox()
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", 12);
        m.AddThemeConstantOverride("margin_right", 12);
        m.AddThemeConstantOverride("margin_top", 8);
        m.AddThemeConstantOverride("margin_bottom", 8);
        return m;
    }

    private static void AddPanelHeader(VBoxContainer parent, string icon, Color iconColor, string title, string hint)
    {
        var header = new PanelContainer { CustomMinimumSize = new Vector2(0, 32) };
        header.AddThemeStyleboxOverride("panel", UiTheme.PanelHeaderStyle());
        var hb = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        header.AddChild(hb);
        hb.AddChild(MakeColoredLabel(icon, 13, iconColor));
        hb.AddChild(MakeColoredLabel(title, 12, Palette.Ink));
        hb.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        hb.AddChild(MakeColoredLabel(hint, 10, Palette.OrnamentInk));
        parent.AddChild(header);
    }

    private void BuildHeroBlock(MarginContainer parent)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        parent.AddChild(v);

        // HP
        var hpRow = new HBoxContainer();
        hpRow.AddChild(MakeColoredLabel("HP", 11, Palette.OrnamentInk));
        _heroHpLabel = MakeColoredLabel("12/12", 11, Palette.Ink);
        hpRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        hpRow.AddChild(_heroHpLabel);
        v.AddChild(hpRow);

        _heroHpBar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0, 6),
            ShowPercentage = false,
            MaxValue = 12,
            Value = 12,
        };
        _heroHpBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = Palette.PaperDark });
        _heroHpBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = Palette.Red });
        v.AddChild(_heroHpBar);

        // 4 屬性方塊
        v.AddChild(MakeColoredLabel("屬性", 11, Palette.OrnamentInk));
        var attrRow = new HBoxContainer();
        attrRow.AddThemeConstantOverride("separation", 6);
        attrRow.AddChild(MakeAttributeBox("戰", "5", Palette.Red));
        attrRow.AddChild(MakeAttributeBox("探", "3", Palette.Green));
        attrRow.AddChild(MakeAttributeBox("社", "1", Palette.Blue));
        attrRow.AddChild(MakeAttributeBox("知", "5", Palette.Purple));
        v.AddChild(attrRow);

        // 主角技能
        v.AddChild(MakeColoredLabel("技能", 11, Palette.OrnamentInk));
        v.AddChild(MakeSkillRow("【主動】威壓 — 消耗 1 AP，目標減 −2"));
        v.AddChild(MakeSkillRow("【被動】洞察 — 觀察首次免費"));
    }

    private static Control MakeAttributeBox(string label, string value, Color accent)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(60, 50) };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.WithAlpha(accent, 0.12f),
            BorderColor = accent,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        var v = new VBoxContainer();
        v.Alignment = BoxContainer.AlignmentMode.Center;
        p.AddChild(v);
        var top = MakeColoredLabel(label, 10, accent);
        top.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(top);
        var num = MakeColoredLabel(value, 18, Palette.Ink);
        num.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(num);
        return p;
    }

    private static Label MakeSkillRow(string text)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", 10);
        l.AddThemeColorOverride("font_color", Palette.InkLight);
        return l;
    }

    private void BuildCompanionPlaceholder(MarginContainer parent)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        parent.AddChild(v);

        for (int i = 0; i < 3; i++)
        {
            var slot = new PanelContainer { CustomMinimumSize = new Vector2(0, 50) };
            slot.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = Palette.WithAlpha(Palette.Paper, 0.4f),
                BorderColor = Palette.Brown,
                BorderWidthLeft = 1, BorderWidthRight = 1,
                BorderWidthTop = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
                CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
                ContentMarginLeft = 8, ContentMarginRight = 8,
                ContentMarginTop = 4, ContentMarginBottom = 4,
            });
            var c = MakeColoredLabel("夥伴空槽", 11, Palette.Brown);
            c.HorizontalAlignment = HorizontalAlignment.Center;
            c.VerticalAlignment = VerticalAlignment.Center;
            slot.AddChild(c);
            v.AddChild(slot);
        }
    }

    private void BuildEquipmentGrid(MarginContainer parent)
    {
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        parent.AddChild(grid);

        // 規格書 §3.4.3 九宮格槽位
        string[] slots = {
            "頭部", "主手", "副手",
            "身體", "飾品", "飾品",
            "足部", "口袋", "背包",
        };
        foreach (var name in slots)
        {
            var cell = new PanelContainer { CustomMinimumSize = new Vector2(80, 80) };
            cell.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = Palette.Paper,
                BorderColor = Palette.InkLight,
                BorderWidthLeft = 1, BorderWidthRight = 1,
                BorderWidthTop = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
                CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            });
            var l = MakeColoredLabel(name, 10, Palette.OrnamentInk);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.VerticalAlignment = VerticalAlignment.Center;
            cell.AddChild(l);
            grid.AddChild(cell);
        }
    }

    public void OnHpChanged(int hp, int hpMax)
    {
        if (_heroHpBar != null)
        {
            _heroHpBar.MaxValue = hpMax;
            _heroHpBar.Value = hp;
        }
        if (_heroHpLabel != null) _heroHpLabel.Text = $"{hp}/{hpMax}";
    }

    private static Label MakeColoredLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}
