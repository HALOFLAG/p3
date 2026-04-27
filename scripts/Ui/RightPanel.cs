using System.Collections.Generic;
using CardNarrative.Core.Map;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// RightPanel — 區塊 #14 NEXT 預覽 / #15 ORBIT / #17 TURN LOG（垂直 3 子面板）。
/// </summary>
public partial class RightPanel : PanelContainer
{
    // Slot 1：未持有時顯示「下一張」（deck 頂端）；持有時顯示「持有」+ 高亮
    private TilePreviewCard? _slot1Card;
    private Label? _slot1TitleLabel;
    private Label? _slot1NameLabel;
    private TilePreviewCard? _slot2Card;
    private Label? _slot2NameLabel;
    private TilePreviewCard? _slot3Card;
    private Label? _slot3NameLabel;
    private Label? _deckRemainingLabel;
    private RichTextLabel? _logText;

    /// <summary>區塊 #15 ORBIT 任務板 — Task 9 起取代舊 stub grid，由 MainBootstrap 注入 EventOrbit。</summary>
    public OrbitPanel? OrbitPanel { get; private set; }

    private readonly List<string> _logLines = new();
    private const int MaxLogLines = 12;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.PaperLight,
            BorderColor = Palette.Ink,
            BorderWidthLeft = 1,
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

        // === 1. NEXT 預覽 ===
        AddPanelHeader(vbox, "✦", Palette.Gold, "NEXT 預覽", "");
        var nextBox = MakeContentBox();
        vbox.AddChild(nextBox);
        BuildNextPreview(nextBox);

        // === 2. ORBIT 軌道（Task 9：取代舊 stub）===
        AddPanelHeader(vbox, "⏳", Palette.RedDark, "ORBIT 軌道", "");
        var orbitBox = MakeContentBox();
        vbox.AddChild(orbitBox);
        OrbitPanel = new OrbitPanel { Name = "OrbitPanel" };
        orbitBox.AddChild(OrbitPanel);

        // === 3. TURN LOG ===
        AddPanelHeader(vbox, "✎", Palette.OrnamentInk, "TURN LOG", "");
        var logBox = MakeContentBox();
        logBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(logBox);
        BuildTurnLog(logBox);
    }

    public void OnDeckStatusChanged(
        int remaining,
        string heldTerrain, string heldName,
        string previewTop, string previewTopName,
        string previewSecond, string previewSecondName,
        string previewThird, string previewThirdName)
    {
        var held = ParseTerrain(heldTerrain);
        if (held is { } heldTile)
        {
            // 持有中：slot1 = 持有（高亮 + 「持有」）；slot2 = deck 頂；slot3 = deck 第 2
            _slot1Card?.SetTerrain(heldTile);
            _slot1Card?.SetHighlighted(true);
            if (_slot1TitleLabel != null) _slot1TitleLabel.Text = "持有";
            SetNameLabel(_slot1NameLabel, heldName, heldTile);
            var top = ParseTerrain(previewTop);
            _slot2Card?.SetTerrain(top);
            SetNameLabel(_slot2NameLabel, previewTopName, top);
            var second = ParseTerrain(previewSecond);
            _slot3Card?.SetTerrain(second);
            SetNameLabel(_slot3NameLabel, previewSecondName, second);
        }
        else
        {
            // 待命：slot1 = 下一張（無高亮）；slot2 = 第 2 張；slot3 = 第 3 張
            var top = ParseTerrain(previewTop);
            _slot1Card?.SetTerrain(top);
            _slot1Card?.SetHighlighted(false);
            if (_slot1TitleLabel != null) _slot1TitleLabel.Text = "下一張";
            SetNameLabel(_slot1NameLabel, previewTopName, top);
            var second = ParseTerrain(previewSecond);
            _slot2Card?.SetTerrain(second);
            SetNameLabel(_slot2NameLabel, previewSecondName, second);
            var third = ParseTerrain(previewThird);
            _slot3Card?.SetTerrain(third);
            SetNameLabel(_slot3NameLabel, previewThirdName, third);
        }
        if (_deckRemainingLabel != null)
            _deckRemainingLabel.Text = $"剩餘 {remaining} 張";
    }

    /// <summary>
    /// 灌卡名 label：優先用 module 提供的中文卡名（如「村內雜貨店」）；
    /// 為空（standalone / 無 module）時 fallback 到地形類別字（「建築」/「森林」）；
    /// 兩者皆無顯示「—」。
    /// </summary>
    private static void SetNameLabel(Label? label, string moduleName, MapTerrain? terrain)
    {
        if (label is null) return;
        if (!string.IsNullOrEmpty(moduleName))
            label.Text = moduleName;
        else if (terrain is { } t)
            label.Text = TilePreviewCard.DisplayName(t);
        else
            label.Text = "—";
    }

    private static MapTerrain? ParseTerrain(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return System.Enum.TryParse<MapTerrain>(s, out var t) ? t : null;
    }

    public void OnLogAppended(string line)
    {
        _logLines.Add(line);
        while (_logLines.Count > MaxLogLines) _logLines.RemoveAt(0);
        if (_logText != null)
        {
            _logText.Text = string.Join('\n', _logLines);
            _logText.ScrollToLine(_logLines.Count - 1);
        }
    }

    private void BuildNextPreview(MarginContainer parent)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        parent.AddChild(v);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        v.AddChild(row);
        // Slot 1：未持有 → 「下一張」；持有時切「持有」+ 高亮（OnDeckStatusChanged 內處理）
        row.AddChild(MakePreviewSlot("下一張", out _slot1Card, out _slot1TitleLabel, out _slot1NameLabel));
        row.AddChild(MakePreviewSlot("第 2 張", out _slot2Card, out _, out _slot2NameLabel));
        row.AddChild(MakePreviewSlot("第 3 張", out _slot3Card, out _, out _slot3NameLabel));

        _deckRemainingLabel = MakeColoredLabel("剩餘 — 張", 10, Palette.OrnamentInk);
        v.AddChild(_deckRemainingLabel);
    }

    private static VBoxContainer MakePreviewSlot(string title, out TilePreviewCard card, out Label titleLabel, out Label nameLabel)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);
        titleLabel = MakeColoredLabel(title, 9, Palette.OrnamentInk);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(titleLabel);
        card = new TilePreviewCard();
        v.AddChild(card);
        // 卡下方卡名 label（如「村內雜貨店」）— 寬度限制在 card 寬以下、超長自動裁切
        nameLabel = MakeColoredLabel("—", 10, Palette.Ink);
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nameLabel.CustomMinimumSize = new Vector2(TilePreviewCard.CardSize, 0);
        nameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        nameLabel.ClipText = true;
        v.AddChild(nameLabel);
        return v;
    }

    private void BuildTurnLog(MarginContainer parent)
    {
        _logText = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollFollowing = true,
            FitContent = false,
            CustomMinimumSize = new Vector2(0, 100),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _logText.AddThemeColorOverride("default_color", Palette.InkLight);
        _logText.AddThemeFontSizeOverride("normal_font_size", 10);
        _logText.Text = "—— TURN LOG 啟動 ——";
        parent.AddChild(_logText);
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

    private static Label MakeColoredLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}
