using CardNarrative.Core.Models;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 整張裝備卡（drag preview 用，未來也可在 slot 內顯示）。
/// 90×130 PanelContainer：類別色帶 + 名稱 + 武器 stats / EffectText。
/// 風格與 CardView 一致（Paper 底 + Ink 邊框）。
/// </summary>
public partial class EquipmentCardView : PanelContainer
{
    public const int CardWidth = 90;
    public const int CardHeight = 130;

    private Label? _categoryLabel;
    private Label? _nameLabel;
    private Label? _bodyLabel;
    private Panel? _categoryBand;

    public override void _Ready()
    {
        if (_nameLabel != null) return; // idempotent
        CustomMinimumSize = new Vector2(CardWidth, CardHeight);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Paper,
            BorderColor = Palette.Ink,
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

        // 類別色帶
        _categoryBand = new Panel { CustomMinimumSize = new Vector2(0, 16), MouseFilter = MouseFilterEnum.Ignore };
        _categoryBand.AddThemeStyleboxOverride("panel", BuildBandStyle(Palette.OrnamentInk));
        vbox.AddChild(_categoryBand);

        _categoryLabel = new Label
        {
            Text = "類別",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _categoryLabel.AddThemeFontSizeOverride("font_size", 10);
        _categoryLabel.AddThemeColorOverride("font_color", Palette.Paper);
        _categoryLabel.AnchorRight = 1;
        _categoryLabel.AnchorBottom = 1;
        _categoryBand.AddChild(_categoryLabel);

        _nameLabel = new Label
        {
            Text = "—",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _nameLabel.AddThemeFontSizeOverride("font_size", 12);
        _nameLabel.AddThemeColorOverride("font_color", Palette.Ink);
        vbox.AddChild(_nameLabel);

        _bodyLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _bodyLabel.AddThemeFontSizeOverride("font_size", 9);
        _bodyLabel.AddThemeColorOverride("font_color", Palette.InkLight);
        _bodyLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(_bodyLabel);
    }

    /// <summary>裝備存在 → 顯示類別/名稱/效果；裝備為 null → 顯示空槽佔位（emptySlotLabel 例：「頭部」「背包 1」）。</summary>
    public void SetEquipment(Equipment? eq, string? emptySlotLabel = null)
    {
        if (_nameLabel is null) _Ready();
        if (eq is null)
        {
            if (_categoryBand != null)
                _categoryBand.AddThemeStyleboxOverride("panel", BuildBandStyle(Palette.WithAlpha(Palette.OrnamentInk, 0.4f)));
            if (_categoryLabel != null) _categoryLabel.Text = emptySlotLabel ?? "—";
            if (_nameLabel != null)
            {
                _nameLabel.Text = "—";
                _nameLabel.AddThemeColorOverride("font_color", Palette.WithAlpha(Palette.OrnamentInk, 0.6f));
            }
            if (_bodyLabel != null) _bodyLabel.Text = "(空槽)";
            return;
        }
        var color = CategoryColor(eq.Category);
        if (_categoryBand != null)
            _categoryBand.AddThemeStyleboxOverride("panel", BuildBandStyle(color));
        if (_categoryLabel != null) _categoryLabel.Text = CategoryDisplayName(eq.Category);
        if (_nameLabel != null)
        {
            _nameLabel.Text = eq.Name;
            _nameLabel.AddThemeColorOverride("font_color", Palette.Ink);
        }
        if (_bodyLabel != null) _bodyLabel.Text = BuildBodyText(eq);
    }

    private static string BuildBodyText(Equipment eq)
    {
        if (eq.Weapon is { } w) return $"命中 +{w.HitBonus}\n傷害 +{w.Damage}";
        if (!string.IsNullOrEmpty(eq.EffectText))
            return eq.EffectText.Length > 28 ? eq.EffectText[..28] + "…" : eq.EffectText;
        if (eq.StatBonuses is { } s)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (s.Power != 0) parts.Add($"戰{s.Power:+#;-#;0}");
            if (s.Social != 0) parts.Add($"社{s.Social:+#;-#;0}");
            if (s.Skill != 0) parts.Add($"探{s.Skill:+#;-#;0}");
            if (s.Intellect != 0) parts.Add($"知{s.Intellect:+#;-#;0}");
            return string.Join("  ", parts);
        }
        return "";
    }

    private static string CategoryDisplayName(ItemCategory c) => c switch
    {
        ItemCategory.Weapon => "武器",
        ItemCategory.Armor => "防具",
        ItemCategory.Accessory => "飾品",
        ItemCategory.Special => "特殊",
        _ => c.ToString(),
    };

    private static Color CategoryColor(ItemCategory c) => c switch
    {
        ItemCategory.Weapon => Palette.Red,
        ItemCategory.Armor => Palette.Green,
        ItemCategory.Accessory => Palette.Brown,
        ItemCategory.Special => Palette.Purple,
        _ => Palette.OrnamentInk,
    };

    private static StyleBoxFlat BuildBandStyle(Color color) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
        CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ContentMarginLeft = 4, ContentMarginRight = 4,
        ContentMarginTop = 1, ContentMarginBottom = 1,
    };
}
