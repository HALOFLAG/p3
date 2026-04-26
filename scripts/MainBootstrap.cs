using System.Collections.Generic;
using System.Linq;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
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
    private ConfirmationDialog? _playCardConfirm;
    private string? _pendingPlayCardId;
    private MainMapRenderer? _mainMap;
    private HandDock? _handDock;

    public override void _Ready()
    {
        // 套用全域 Theme
        Theme = UiTheme.Build();

        // 找各子場景節點
        var topBar = GetNodeOrNull<TopBar>("ScreenStack/TopBar");
        var leftPanel = GetNodeOrNull<LeftPanel>("ScreenStack/BodyRow/LeftPanel");
        var rightPanel = GetNodeOrNull<RightPanel>("ScreenStack/BodyRow/RightPanel");
        var mainMap = GetNodeOrNull<MainMapRenderer>("ScreenStack/BodyRow/MiddleColumn/MapArea/MainMap");
        var handDock = GetNodeOrNull<HandDock>("ScreenStack/BodyRow/MiddleColumn/HandDock");
        _mainMap = mainMap;
        _handDock = handDock;

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
            // 裝備：拖曳請求 → MainMap；MainMap 變更 → 重繪 LeftPanel
            leftPanel.EquipmentMoveRequested += mainMap.RequestMoveEquipment;
            mainMap.EquipmentChangedExt += () => leftPanel.OnEquipmentChanged(
                mainMap.WorldMap.Equipped,
                mainMap.WorldMap.Backpack,
                mainMap.WorldMap.EquipmentCatalog);

            // 背包展開時把 slots 1, 2 + frame reparent 到此 overlay，
            // 避開父層 grid cell 的 hit-test rect 限制（讓 drag 在展開區域可用）
            var backpackOverlay = new Control
            {
                Name = "BackpackOverlay",
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(backpackOverlay);
            backpackOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            leftPanel.SetBackpackOverlay(backpackOverlay);
        }

        if (rightPanel != null)
        {
            mainMap.DeckStatusChanged += rightPanel.OnDeckStatusChanged;
            mainMap.LogAppended += rightPanel.OnLogAppended;
        }

        // === HandDock 接線 ===
        if (handDock != null)
        {
            mainMap.HandCardsChanged += handDock.OnHandCardsChanged;
            mainMap.ActionDeckCountsChanged += handDock.OnDeckCountsChanged;
            // 出牌完全走拖曳：不再接 CardClickedExt 跳對話框
        }

        // === 出牌確認對話框（動態建立，避免 main.tscn 又被 Godot Editor 蓋掉） ===
        _playCardConfirm = new ConfirmationDialog
        {
            Title = "",
            Theme = UiTheme.Build(),
        };
        _playCardConfirm.Borderless = true;
        AddChild(_playCardConfirm);
        UiTheme.ApplyPrimaryButtonStyle(_playCardConfirm.GetOkButton());
        _playCardConfirm.Confirmed += OnPlayCardConfirmed;
        _playCardConfirm.Canceled += () => _pendingPlayCardId = null;
        _playCardConfirm.VisibilityChanged += () =>
        {
            if (_modalOverlay != null && _playCardConfirm.Visible)
                _modalOverlay.Visible = true;
            else if (_modalOverlay != null && !_playCardConfirm.Visible
                     && (mainMap.GetNodeOrNull<ConfirmationDialog>("MoveConfirmDialog")?.Visible ?? false) == false)
                _modalOverlay.Visible = false;
        };

        // === 載入 abandoned-mansion 模組行動卡 ===
        TryLoadAbandonedMansionDeck(mainMap);

        GD.Print("[MainBootstrap] 主場景就緒，所有 Signal 已連線。");
    }

    private void TryLoadAbandonedMansionDeck(MainMapRenderer mainMap)
    {
        try
        {
            var schemasFolder = ProjectSettings.GlobalizePath("res://core/schemas/");
            var modulePath = ProjectSettings.GlobalizePath("res://modules/builtin/abandoned-mansion/");
            var loader = new ModuleLoader(schemasFolder);
            var result = loader.Load(modulePath);
            if (result is ModuleLoadResult.Success success)
            {
                // scholar 起手 8 張作為 demo deck
                var deck = success.Module.ActionCards.Values
                    .Where(c => c.Id.StartsWith("scholar-"))
                    .Take(8)
                    .ToList();
                if (deck.Count == 0)
                    deck = success.Module.ActionCards.Values.Take(8).ToList();
                mainMap.LoadActionDeck(deck);
                GD.Print($"[MainBootstrap] 載入模組成功：{deck.Count} 張行動卡進入牌堆。");

                // 裝備：丟前 3 件進背包當 demo（規格書 §3.4.3 獲得即入背包）
                var equipmentCatalog = success.Module.Equipment;
                var initialBackpack = equipmentCatalog.Values.Take(3).Select(e => e.Id).ToList();
                mainMap.LoadEquipmentInventory(equipmentCatalog, initialBackpack);
                GD.Print($"[MainBootstrap] 載入裝備目錄：{equipmentCatalog.Count} 件，初始背包 {initialBackpack.Count} 件。");
            }
            else if (result is ModuleLoadResult.Failure fail)
            {
                GD.PrintErr($"[MainBootstrap] 模組載入失敗：{fail.Errors.Count} 個錯誤");
                foreach (var err in fail.Errors) GD.PrintErr($"  - {err}");
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[MainBootstrap] 模組載入例外：{ex.Message}");
        }
    }

    private void OnHandCardClicked(string cardId)
    {
        if (_playCardConfirm is null || _mainMap is null) return;
        var card = _mainMap.WorldMap.Hand.FirstOrDefault(c => c.Id == cardId);
        if (card is null) return;
        _pendingPlayCardId = cardId;
        _playCardConfirm.DialogText = $"【 出牌 】\n\n打出「{card.Name}」？\n消耗：{card.Cost} AP";
        _playCardConfirm.PopupCentered();
    }

    private void OnPlayCardConfirmed()
    {
        if (_pendingPlayCardId is null || _mainMap is null) return;
        _mainMap.RequestPlayCard(_pendingPlayCardId);
        _pendingPlayCardId = null;
    }
}
