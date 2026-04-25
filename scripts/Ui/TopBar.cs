using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 區塊 #1/#2/#3/#5/#6 — TopBar（規格書 §4.1 + 用戶版面）。
/// 左：✦廢棄洋房調查 ★★★ + 勝利條件鈕
/// 中：第 X 回合 + NEXT TURN ▶
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

    public override void _Ready()
    {
        // 套用 PaperDark 32px 樣式但拉高為 48
        var style = UiTheme.PanelHeaderStyle();
        style.BgColor = Palette.Paper;
        style.ContentMarginTop = 8;
        style.ContentMarginBottom = 8;
        AddThemeStyleboxOverride("panel", style);

        var hbox = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        hbox.AddThemeConstantOverride("separation", 12);
        AddChild(hbox);

        // === 左區 ===
        var leftBox = new VBoxContainer();
        leftBox.AddThemeConstantOverride("separation", 2);
        hbox.AddChild(leftBox);

        var titleLbl = MakeLabel("✦ 廢棄洋房調查 ★★★", 14, Palette.Ink);
        leftBox.AddChild(titleLbl);

        var victoryBtn = new Button { Text = "勝利條件" };
        victoryBtn.AddThemeFontSizeOverride("font_size", 10);
        victoryBtn.Pressed += () => EmitSignal(SignalName.VictoryConditionsPressed);
        leftBox.AddChild(victoryBtn);

        // === 分隔 ===
        AddDivider(hbox);

        // === 中區 ===
        var midBox = new HBoxContainer();
        midBox.AddThemeConstantOverride("separation", 8);
        hbox.AddChild(midBox);

        _turnLabel = MakeLabel("第 1 回合", 13, Palette.Ink);
        midBox.AddChild(_turnLabel);

        _modeLabel = MakeLabel("(待命)", 11, Palette.OrnamentInk);
        midBox.AddChild(_modeLabel);

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
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        hbox.AddChild(spacer);

        var optionsBtn = new Button { Text = "⚙ 選項" };
        optionsBtn.AddThemeFontSizeOverride("font_size", 11);
        optionsBtn.Pressed += () => EmitSignal(SignalName.OptionsPressed);
        hbox.AddChild(optionsBtn);
    }

    public void OnPlayerPositionChanged(int row, int col)
    {
        // demo：暫無回合計數，先顯示位置
    }

    public void OnModeChanged(string modeLabel)
    {
        if (_modeLabel != null) _modeLabel.Text = $"({modeLabel})";
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
