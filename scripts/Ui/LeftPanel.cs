using System.Collections.Generic;
using CardNarrative.Core.Models;
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
    [Signal] public delegate void EquipmentMoveRequestedEventHandler(string sourceKind, int sourceIdx, string targetKind, int targetIdx);

    private ProgressBar? _heroHpBar;
    private Label? _heroHpLabel;
    private readonly Dictionary<EquipmentSlot, EquipmentSlotView> _primarySlots = new();
    private BackpackBar? _backpackBar;

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
        AddPanelHeader(vbox, "○", Palette.Blue, "夥伴", "0/1");
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

        for (int i = 0; i < 1; i++)
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
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        parent.AddChild(vbox);

        // 規格書 §3.4.3：3×3 主槽（8 個）+ 1 背包欄
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", BackpackBar.Gap);
        grid.AddThemeConstantOverride("v_separation", BackpackBar.Gap);
        grid.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        vbox.AddChild(grid);

        var slotDefs = new (EquipmentSlot slot, string label)[]
        {
            (EquipmentSlot.Head, "頭部"),
            (EquipmentSlot.Weapon, "主手"),
            (EquipmentSlot.OffHand, "副手"),
            (EquipmentSlot.Body, "身體"),
            (EquipmentSlot.AccessoryA, "飾品 A"),
            (EquipmentSlot.AccessoryB, "飾品 B"),
            (EquipmentSlot.Feet, "足部"),
            (EquipmentSlot.Utility, "口袋"),
        };

        foreach (var (slot, label) in slotDefs)
        {
            var view = new EquipmentSlotView();
            grid.AddChild(view);
            view.ConfigurePrimary(slot, label);
            view.EquipmentDropped += OnEquipmentDropped;
            _primarySlots[slot] = view;
        }
        // 第 9 格：背包欄（摺疊狀態 = 1 格寬 + `>` 按鈕；展開時向右浮出）
        _backpackBar = new BackpackBar();
        grid.AddChild(_backpackBar);
        _backpackBar.EquipmentDropped += OnEquipmentDropped;
    }

    private void OnEquipmentDropped(string sourceKind, int sourceIdx, string targetKind, int targetIdx)
    {
        EmitSignal(SignalName.EquipmentMoveRequested, sourceKind, sourceIdx, targetKind, targetIdx);
    }

    /// <summary>由 MainBootstrap 提供 root-level overlay，給 BackpackBar 在展開時 reparent overflow content。</summary>
    public void SetBackpackOverlay(Control overlay)
    {
        _backpackBar?.SetTopOverlay(overlay);
    }

    public void OnEquipmentChanged(
        IReadOnlyDictionary<EquipmentSlot, string?> equipped,
        IReadOnlyList<string> backpack,
        IReadOnlyDictionary<string, Equipment> catalog)
    {
        foreach (var (slot, view) in _primarySlots)
        {
            equipped.TryGetValue(slot, out var eqId);
            Equipment? item = null;
            if (eqId is not null) catalog.TryGetValue(eqId, out item);
            view.SetEquipment(eqId, item);
            view.Catalog = catalog;
        }
        _backpackBar?.UpdateContents(backpack, catalog);
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
