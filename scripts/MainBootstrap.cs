using Godot;
using HauntedManor.Scripts.Map;
using HauntedManor.Scripts.Theme;
using HauntedManor.Scripts.Ui;

namespace HauntedManor.Scripts;

/// <summary>
/// 主場景控制器：套用 Theme + 把 MainMapRenderer 的 Signal 接到 TopBar / LeftPanel / RightPanel。
/// 額外負責：對話框顯示時提供全螢幕 modal overlay（取代被 Window clip 掉的 shadow）。
/// </summary>
public partial class MainBootstrap : Control
{
    private ColorRect? _modalOverlay;

    public override void _Ready()
    {
        // 套用全域 Theme
        Theme = UiTheme.Build();

        // 找各子場景節點
        var topBar = GetNodeOrNull<TopBar>("ScreenStack/TopBar");
        var leftPanel = GetNodeOrNull<LeftPanel>("ScreenStack/BodyRow/LeftPanel");
        var rightPanel = GetNodeOrNull<RightPanel>("ScreenStack/BodyRow/RightPanel");
        var mainMap = GetNodeOrNull<MainMapRenderer>("ScreenStack/BodyRow/MiddleColumn/MapArea/MainMap");

        if (mainMap is null)
        {
            GD.PrintErr("[MainBootstrap] 找不到 MainMap 節點");
            return;
        }

        // === Modal overlay（全螢幕暗色，對話框顯示時用）===
        _modalOverlay = new ColorRect
        {
            Color = Palette.WithAlpha(Palette.Ink, 0.25f),
            MouseFilter = MouseFilterEnum.Stop, // 攔截下層點擊
            Visible = false,
            Name = "ModalOverlay",
        };
        AddChild(_modalOverlay);
        _modalOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Hook 對話框可見性 → overlay 同步
        var moveConfirm = mainMap.GetNodeOrNull<ConfirmationDialog>("MoveConfirmDialog");
        if (moveConfirm != null)
        {
            moveConfirm.VisibilityChanged += () =>
            {
                if (_modalOverlay != null)
                {
                    _modalOverlay.Visible = moveConfirm.Visible;
                }
            };
        }

        // 串接 signal：MainMap → TopBar / LeftPanel / RightPanel
        if (topBar != null)
        {
            mainMap.PlayerPositionChanged += topBar.OnPlayerPositionChanged;
            mainMap.ModeChangedExt += topBar.OnModeChanged;
            mainMap.TurnChangedExt += topBar.OnTurnChanged;
            mainMap.ApChangedExt += topBar.OnApChanged;
            mainMap.HandChangedExt += topBar.OnHandChanged;
            // TopBar 按鈕反向觸發 MainMap
            topBar.DrawTilePressed += mainMap.RequestDrawTile;
            topBar.EndTurnPressed += mainMap.RequestAdvanceTurn; // Task 6：真正推進回合
            topBar.OptionsPressed += () => GD.Print("[MainBootstrap] Options 按下（待後續實作）");
            topBar.VictoryConditionsPressed += () => GD.Print("[MainBootstrap] VictoryConditions 按下（待後續實作）");
        }

        if (leftPanel != null)
        {
            mainMap.HpChangedExt += leftPanel.OnHpChanged;
        }

        if (rightPanel != null)
        {
            mainMap.DeckStatusChanged += rightPanel.OnDeckStatusChanged;
            mainMap.LogAppended += rightPanel.OnLogAppended;
        }

        GD.Print("[MainBootstrap] 主場景就緒，所有 Signal 已連線。");
    }
}
