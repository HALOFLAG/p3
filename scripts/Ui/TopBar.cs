using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 區塊 #1/#2/#3/#5/#6 — TopBar（規格書 §4.1 + 用戶版面）。
/// 左：✦廢棄洋房調查 ★★★ + 勝利條件鈕
/// 中：第 X 回合 + 模式 + AP pips + 手牌 + 抽地塊 + NEXT TURN ▶
/// 右：選項
/// </summary>
public partial class TopBar : PanelContainer
{
    [Signal] public delegate void DrawTilePressedEventHandler();
    [Signal] public delegate void EndTurnPressedEventHandler();
    [Signal] public delegate void OptionsPressedEventHandler();
    [Signal] public delegate void VictoryConditionsPressedEventHandler();

    private Label? _turnLabel;
    private Label? _modeLabel;
    private Label? _handLabel;
    private HBoxContainer? _apBar;
    private int _apMaxCache = 3;

    public override void _Ready()
    {
        // PaperDark 樣式
        var style = UiTheme.PanelHeaderStyle();
        style.BgColor = Palette.Paper;
        style.ContentMarginTop = 8;
        style.ContentMarginBottom = 8;
        AddThemeStyleboxOverride("panel", style);

        var hbox = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        hbox.AddThemeConstantOverride("separation", 12);
        AddChild(hbox);

        // === 左區：標題 + 勝利條件 ===
        var leftBox = new VBoxContainer();
        leftBox.AddThemeConstantOverride("separation", 2);
        hbox.AddChild(leftBox);

        leftBox.AddChild(MakeLabel("✦ 廢棄洋房調查 ★★★", 14, Palette.Ink));

        var victoryBtn = new Button { Text = "勝利條件" };
        victoryBtn.AddThemeFontSizeOverride("font_size", 10);
        victoryBtn.Pressed += () => EmitSignal(SignalName.VictoryConditionsPressed);
        leftBox.AddChild(victoryBtn);

        AddDivider(hbox);

        // === 中區：回合 + 模式 + AP + 手牌 + 動作 ===
        var midBox = new HBoxContainer();
        midBox.AddThemeConstantOverride("separation", 8);
        hbox.AddChild(midBox);

        _turnLabel = MakeLabel("第 1 回合", 13, Palette.Ink);
        midBox.AddChild(_turnLabel);

        _modeLabel = MakeLabel("(待命)", 11, Palette.OrnamentInk);
        midBox.AddChild(_modeLabel);

        AddDivider(midBox);

        // AP pips：用 HBox + ColorRect 圓點
        midBox.AddChild(MakeLabel("AP", 11, Palette.OrnamentInk));
        _apBar = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _apBar.AddThemeConstantOverride("separation", 3);
        midBox.AddChild(_apBar);
        BuildApPips(3, 3); // 預設

        AddDivider(midBox);

        midBox.AddChild(MakeLabel("手牌", 11, Palette.OrnamentInk));
        _handLabel = MakeLabel("5/5", 11, Palette.Ink);
        midBox.AddChild(_handLabel);

        AddDivider(midBox);

        var drawBtn = new Button { Text = "抽地塊" };
        drawBtn.AddThemeFontSizeOverride("font_size", 11);
        drawBtn.Pressed += () => EmitSignal(SignalName.DrawTilePressed);
        midBox.AddChild(drawBtn);

        var nextTurnBtn = new Button { Text = "NEXT TURN ▶" };
        nextTurnBtn.AddThemeFontSizeOverride("font_size", 11);
        UiTheme.ApplyPrimaryButtonStyle(nextTurnBtn);
        nextTurnBtn.Pressed += () => EmitSignal(SignalName.EndTurnPressed);
        midBox.AddChild(nextTurnBtn);

        // === 右側填充 + 選項 ===
        hbox.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var optionsBtn = new Button { Text = "⚙ 選項" };
        optionsBtn.AddThemeFontSizeOverride("font_size", 11);
        optionsBtn.Pressed += () => EmitSignal(SignalName.OptionsPressed);
        hbox.AddChild(optionsBtn);
    }

    public void OnPlayerPositionChanged(int row, int col)
    {
        // 暫不在 TopBar 顯示位置（在 RightPanel TURN LOG 看到即可）
    }

    public void OnModeChanged(string modeLabel)
    {
        if (_modeLabel != null) _modeLabel.Text = $"({modeLabel})";
    }

    public void OnTurnChanged(int turn, int turnLimit)
    {
        if (_turnLabel != null) _turnLabel.Text = $"第 {turn} / {turnLimit} 回合";
    }

    public void OnApChanged(int ap, int apMax)
    {
        if (_apBar is null) return;
        if (apMax != _apMaxCache)
        {
            _apMaxCache = apMax;
            BuildApPips(ap, apMax);
            return;
        }
        // 只更新顯示狀態
        for (int i = 0; i < _apBar.GetChildCount(); i++)
        {
            if (_apBar.GetChild(i) is ColorRect pip)
            {
                pip.Color = i < ap ? Palette.Gold : Palette.WithAlpha(Palette.Brown, 0.3f);
            }
        }
    }

    public void OnHandChanged(int hand, int handMax)
    {
        if (_handLabel != null) _handLabel.Text = $"{hand}/{handMax}";
    }

    private void BuildApPips(int ap, int apMax)
    {
        if (_apBar is null) return;
        // 清掉舊
        foreach (var child in _apBar.GetChildren())
        {
            child.QueueFree();
        }
        for (int i = 0; i < apMax; i++)
        {
            var pip = new ColorRect
            {
                CustomMinimumSize = new Vector2(12, 12),
                Color = i < ap ? Palette.Gold : Palette.WithAlpha(Palette.Brown, 0.3f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _apBar.AddChild(pip);
        }
    }

    private static Label MakeLabel(string text, int size, Color color)
    {
        var lbl = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        lbl.AddThemeFontSizeOverride("font_size", size);
        lbl.AddThemeColorOverride("font_color", color);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        return lbl;
    }

    private static void AddDivider(HBoxContainer parent)
    {
        var div = new ColorRect
        {
            Color = Palette.WithAlpha(Palette.Ink, 0.25f),
            CustomMinimumSize = new Vector2(1, 24),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(div);
    }
}
