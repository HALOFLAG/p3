using CardNarrative.Core.Models;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 角色卡視覺呈現（占用裝備格的卡片，金色色帶與裝備卡區分）。
/// 90×130，與 EquipmentCardView 同尺寸。
/// 顯示內容：色帶「角色」+ 四屬性 2×2 方格（戰/社/探/知）。
/// 名稱 / Specialty / 技能 已從 LeftPanel 主角區顯示，本卡只表示「角色卡占用此格」與當下基礎屬性。
/// </summary>
public partial class CharacterCardView : PanelContainer
{
    public const int CardWidth = 90;
    public const int CardHeight = 130;

    private Label? _powerValue;
    private Label? _socialValue;
    private Label? _skillValue;
    private Label? _intellectValue;

    public override void _Ready()
    {
        if (_powerValue != null) return; // idempotent
        CustomMinimumSize = new Vector2(CardWidth, CardHeight);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Paper,
            BorderColor = Palette.Gold,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        MouseFilter = MouseFilterEnum.Ignore;

        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.AddThemeConstantOverride("separation", 3);
        AddChild(vbox);

        // 色帶：角色
        var band = new Panel { CustomMinimumSize = new Vector2(0, 16), MouseFilter = MouseFilterEnum.Ignore };
        band.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Gold,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 1, ContentMarginBottom = 1,
        });
        vbox.AddChild(band);
        var bandLabel = new Label
        {
            Text = "角色",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        bandLabel.AddThemeFontSizeOverride("font_size", 10);
        bandLabel.AddThemeColorOverride("font_color", Palette.Paper);
        band.AddChild(bandLabel);

        // 四屬性 2×2 方格
        var grid = new GridContainer
        {
            Columns = 2,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        vbox.AddChild(grid);

        _powerValue = MakeValueLabel();
        _socialValue = MakeValueLabel();
        _skillValue = MakeValueLabel();
        _intellectValue = MakeValueLabel();
        grid.AddChild(MakeStatCell("戰", _powerValue, Palette.Red));
        grid.AddChild(MakeStatCell("社", _socialValue, Palette.Blue));
        grid.AddChild(MakeStatCell("探", _skillValue, Palette.Green));
        grid.AddChild(MakeStatCell("知", _intellectValue, Palette.Purple));
    }

    public void SetCharacter(Character? c)
    {
        if (_powerValue is null) _Ready();
        if (c is null)
        {
            if (_powerValue != null) _powerValue.Text = "—";
            if (_socialValue != null) _socialValue.Text = "—";
            if (_skillValue != null) _skillValue.Text = "—";
            if (_intellectValue != null) _intellectValue.Text = "—";
            return;
        }
        if (_powerValue != null) _powerValue.Text = c.Stats.Power.ToString();
        if (_socialValue != null) _socialValue.Text = c.Stats.Social.ToString();
        if (_skillValue != null) _skillValue.Text = c.Stats.Skill.ToString();
        if (_intellectValue != null) _intellectValue.Text = c.Stats.Intellect.ToString();
    }

    private static Label MakeValueLabel()
    {
        var l = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", 16);
        l.AddThemeColorOverride("font_color", Palette.Ink);
        return l;
    }

    private static Control MakeStatCell(string label, Label valueLabel, Color accent)
    {
        var p = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.WithAlpha(accent, 0.12f),
            BorderColor = accent,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            ContentMarginLeft = 2, ContentMarginRight = 2,
            ContentMarginTop = 1, ContentMarginBottom = 1,
        });
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.Alignment = BoxContainer.AlignmentMode.Center;
        v.AddThemeConstantOverride("separation", 0);
        p.AddChild(v);
        var top = new Label
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        top.AddThemeFontSizeOverride("font_size", 9);
        top.AddThemeColorOverride("font_color", accent);
        v.AddChild(top);
        v.AddChild(valueLabel);
        return p;
    }
}
