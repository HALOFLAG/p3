using System.Collections.Generic;
using CardNarrative.Core.Models;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 區塊 #7 + #8 — LeftPanel（依用戶版面，垂直 3 區）。
/// 1) 主角區：HP 條 + 4 屬性方塊（紅綠藍紫）+ 主角技能（資料來源：Character + 裝備加值）
/// 2) 夥伴區：1~3 個夥伴 HP + 性格 + 技能（資料來源：NpcCompanion）
/// 3) 裝備卡 3×3 九宮格（含一格被角色卡占住）
/// </summary>
public partial class LeftPanel : PanelContainer
{
    [Signal] public delegate void EquipmentMoveRequestedEventHandler(string sourceKind, int sourceIdx, string targetKind, int targetIdx);

    // Hero block
    private ProgressBar? _heroHpBar;
    private Label? _heroHpLabel;
    private Label? _heroNameLabel;
    private Label? _heroPower;
    private Label? _heroSocial;
    private Label? _heroSkill;
    private Label? _heroIntellect;
    private Label? _heroHeaderHint;

    // Companion block
    private VBoxContainer? _companionBox;
    private Label? _companionHeaderHint;

    // Equipment grid
    private readonly Dictionary<EquipmentSlot, EquipmentSlotView> _primarySlots = new();
    private BackpackBar? _backpackBar;

    // Cached character card slot (set by OnCharacterChanged); used by OnEquipmentChanged
    // to know which primary slot should render the character card view.
    private Character? _currentCharacter;
    private EquipmentSlot _characterCardSlot = EquipmentSlot.Head;

    public override void _Ready()
    {
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
        _heroHeaderHint = MakeColoredLabel("—", 10, Palette.OrnamentInk);
        AddPanelHeader(vbox, "◆", Palette.Gold, "主角", _heroHeaderHint);
        var heroBox = MakeContentBox();
        vbox.AddChild(heroBox);
        BuildHeroBlock(heroBox);

        // === 2. 夥伴區 ===
        _companionHeaderHint = MakeColoredLabel("0/1", 10, Palette.OrnamentInk);
        AddPanelHeader(vbox, "○", Palette.Blue, "夥伴", _companionHeaderHint);
        var companionParent = MakeContentBox();
        vbox.AddChild(companionParent);
        _companionBox = new VBoxContainer();
        _companionBox.AddThemeConstantOverride("separation", 6);
        companionParent.AddChild(_companionBox);
        RenderCompanionEmpty();

        // === 3. 裝備卡 3×3 九宮格 ===
        AddPanelHeader(vbox, "✦", Palette.Gold, "裝備", MakeColoredLabel("3×3", 10, Palette.OrnamentInk));
        var equipBox = MakeContentBox();
        vbox.AddChild(equipBox);
        BuildEquipmentGrid(equipBox);

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

    private static void AddPanelHeader(VBoxContainer parent, string icon, Color iconColor, string title, Label hintLabel)
    {
        var header = new PanelContainer { CustomMinimumSize = new Vector2(0, 32) };
        header.AddThemeStyleboxOverride("panel", UiTheme.PanelHeaderStyle());
        var hb = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        header.AddChild(hb);
        hb.AddChild(MakeColoredLabel(icon, 13, iconColor));
        hb.AddChild(MakeColoredLabel(title, 12, Palette.Ink));
        hb.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        hb.AddChild(hintLabel);
        parent.AddChild(header);
    }

    private void BuildHeroBlock(MarginContainer parent)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        parent.AddChild(v);

        // 名稱
        _heroNameLabel = MakeColoredLabel("—", 12, Palette.Ink);
        v.AddChild(_heroNameLabel);

        // HP
        var hpRow = new HBoxContainer();
        hpRow.AddChild(MakeColoredLabel("HP", 11, Palette.OrnamentInk));
        _heroHpLabel = MakeColoredLabel("0/0", 11, Palette.Ink);
        hpRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        hpRow.AddChild(_heroHpLabel);
        v.AddChild(hpRow);

        _heroHpBar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0, 6),
            ShowPercentage = false,
            MaxValue = 1,
            Value = 0,
        };
        _heroHpBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = Palette.PaperDark });
        _heroHpBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = Palette.Red });
        v.AddChild(_heroHpBar);

        // 4 屬性方塊（顯示 = 角色 base + 裝備加總）
        v.AddChild(MakeColoredLabel("屬性", 11, Palette.OrnamentInk));
        var attrRow = new HBoxContainer();
        attrRow.AddThemeConstantOverride("separation", 6);
        _heroPower = MakeStatValueLabel();
        _heroSocial = MakeStatValueLabel();
        _heroSkill = MakeStatValueLabel();
        _heroIntellect = MakeStatValueLabel();
        attrRow.AddChild(MakeAttributeBox("戰", _heroPower, Palette.Red));
        attrRow.AddChild(MakeAttributeBox("社", _heroSocial, Palette.Blue));
        attrRow.AddChild(MakeAttributeBox("探", _heroSkill, Palette.Green));
        attrRow.AddChild(MakeAttributeBox("知", _heroIntellect, Palette.Purple));
        v.AddChild(attrRow);
    }

    private static Label MakeStatValueLabel()
    {
        var l = new Label { Text = "0", MouseFilter = Control.MouseFilterEnum.Ignore };
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.AddThemeFontSizeOverride("font_size", 18);
        l.AddThemeColorOverride("font_color", Palette.Ink);
        return l;
    }

    private static Control MakeAttributeBox(string label, Label valueLabel, Color accent)
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
        v.AddChild(valueLabel);
        return p;
    }

    private static Label MakeSkillRow(string text, Color color)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", 10);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private void RenderCompanionEmpty()
    {
        if (_companionBox is null) return;
        ClearChildren(_companionBox);
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
        _companionBox.AddChild(slot);
    }

    private static void ClearChildren(Node parent)
    {
        foreach (var c in parent.GetChildren()) c.QueueFree();
    }

    private void BuildEquipmentGrid(MarginContainer parent)
    {
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        parent.AddChild(vbox);

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
        _backpackBar = new BackpackBar();
        grid.AddChild(_backpackBar);
        _backpackBar.EquipmentDropped += OnEquipmentDropped;
    }

    private void OnEquipmentDropped(string sourceKind, int sourceIdx, string targetKind, int targetIdx)
    {
        EmitSignal(SignalName.EquipmentMoveRequested, sourceKind, sourceIdx, targetKind, targetIdx);
    }

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

            if (slot == _characterCardSlot && _currentCharacter is not null)
            {
                view.SetCharacterCard(_currentCharacter);
            }
            else
            {
                view.SetEquipment(eqId, item);
            }
            view.Catalog = catalog;
        }
        _backpackBar?.UpdateContents(backpack, catalog);
    }

    /// <summary>主角區資料更新（角色 + HP + 已加總 4 屬性 + 技能列）。</summary>
    public void OnCharacterChanged(
        Character character,
        int hp,
        int hpMax,
        StatBlock totalStats,
        EquipmentSlot characterCardSlot)
    {
        _currentCharacter = character;
        _characterCardSlot = characterCardSlot;

        if (_heroNameLabel != null) _heroNameLabel.Text = character.Name;
        if (_heroHeaderHint != null) _heroHeaderHint.Text = character.Specialty.Length > 16
            ? character.Specialty[..16] + "…"
            : character.Specialty;
        if (_heroHpBar != null)
        {
            _heroHpBar.MaxValue = hpMax;
            _heroHpBar.Value = hp;
        }
        if (_heroHpLabel != null) _heroHpLabel.Text = $"{hp}/{hpMax}";
        if (_heroPower != null) _heroPower.Text = totalStats.Power.ToString();
        if (_heroSocial != null) _heroSocial.Text = totalStats.Social.ToString();
        if (_heroSkill != null) _heroSkill.Text = totalStats.Skill.ToString();
        if (_heroIntellect != null) _heroIntellect.Text = totalStats.Intellect.ToString();
    }

    /// <summary>夥伴區資料更新（夥伴卡或 null）。</summary>
    public void OnCompanionChanged(NpcCompanion? companion, int hp, int hpMax)
    {
        if (_companionBox is null) return;
        ClearChildren(_companionBox);
        if (companion is null)
        {
            RenderCompanionEmpty();
            if (_companionHeaderHint != null) _companionHeaderHint.Text = "0/1";
            return;
        }

        if (_companionHeaderHint != null) _companionHeaderHint.Text = "1/1";

        // 夥伴名 + HP
        var nameRow = new HBoxContainer();
        nameRow.AddChild(MakeColoredLabel(companion.Name, 12, Palette.Ink));
        nameRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        nameRow.AddChild(MakeColoredLabel($"HP {hp}/{hpMax}", 11, Palette.OrnamentInk));
        _companionBox.AddChild(nameRow);

        var hpBar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0, 5),
            ShowPercentage = false,
            MaxValue = hpMax > 0 ? hpMax : 1,
            Value = hp,
        };
        hpBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = Palette.PaperDark });
        hpBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = Palette.Brown });
        _companionBox.AddChild(hpBar);

        if (!string.IsNullOrEmpty(companion.Personality))
        {
            _companionBox.AddChild(MakeSkillRow($"性格：{companion.Personality}", Palette.OrnamentInk));
        }
        if (!string.IsNullOrEmpty(companion.Specialty))
        {
            _companionBox.AddChild(MakeSkillRow($"專長：{companion.Specialty}", Palette.InkLight));
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
