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
    private TilePreviewCard? _slot2Card;
    private TilePreviewCard? _slot3Card;
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

    public void OnDeckStatusChanged(int remaining, string heldTerrain, string previewTop, string previewSecond, string previewThird)
    {
        var held = ParseTerrain(heldTerrain);
        if (held is { } heldTile)
        {
            // 持有中：slot1 = 持有（高亮 + 「持有」）；slot2 = deck 頂；slot3 = deck 第 2
            _slot1Card?.SetTerrain(heldTile);
            _slot1Card?.SetHighlighted(true);
            if (_slot1TitleLabel != null) _slot1TitleLabel.Text = "持有";
            _slot2Card?.SetTerrain(ParseTerrain(previewTop));
            _slot3Card?.SetTerrain(ParseTerrain(previewSecond));
        }
        else
        {
            // 待命：slot1 = 下一張（無高亮）；slot2 = 第 2 張；slot3 = 第 3 張
            _slot1Card?.SetTerrain(ParseTerrain(previewTop));
            _slot1Card?.SetHighlighted(false);
            if (_slot1TitleLabel != null) _slot1TitleLabel.Text = "下一張";
            _slot2Card?.SetTerrain(ParseTerrain(previewSecond));
            _slot3Card?.SetTerrain(ParseTerrain(previewThird));
        }
        if (_deckRemainingLabel != null)
            _deckRemainingLabel.Text = $"剩餘 {remaining} 張";
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
        row.AddChild(MakePreviewSlot("下一張", out _slot1Card, out _slot1TitleLabel));
        row.AddChild(MakePreviewSlot("第 2 張", out _slot2Card, out _));
        row.AddChild(MakePreviewSlot("第 3 張", out _slot3Card, out _));

        _deckRemainingLabel = MakeColoredLabel("剩餘 — 張", 10, Palette.OrnamentInk);
        v.AddChild(_deckRemainingLabel);
    }

    private static VBoxContainer MakePreviewSlot(string title, out TilePreviewCard card, out Label titleLabel)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);
        titleLabel = MakeColoredLabel(title, 9, Palette.OrnamentInk);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(titleLabel);
        card = new TilePreviewCard();
        v.AddChild(card);
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
