using System.Collections.Generic;
using System.Linq;
using CardNarrative.Core.Events;
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

    // Task 9 · ORBIT 軌道（Core 物件 + UI 面板）
    private EventBus? _eventBus;
    private EventOrbit? _orbit;
    private EventOrbitResolver? _orbitResolver;
    private OrbitPanel? _orbitPanel;
    private EventResolutionDialog? _eventDialog;
    private CardNarrative.Core.Models.Module? _module;
    // Task 11 Stage 1：runtime GameState（gridSize=9, startPosition=(4,4)）；
    // Stage 1 僅儲存參照供 Stage 2 façade 使用，本 stage WorldMap 仍是 authoritative。
    private CardNarrative.Core.State.GameState? _gameState;
    private readonly System.Random _orbitDemoRng = new();

    public override void _Ready()
    {
        // 套用全域 Theme
        Theme = UiTheme.Build();

        // === Task 9 · EventBus + ORBIT 物件（不動 main.tscn）===
        // OrbitPanel UI 由 RightPanel 的 ORBIT 區位內建，不需此處浮動建立。
        _eventBus = new EventBus { Name = "EventBus" };
        AddChild(_eventBus);
        _orbit = new EventOrbit();
        _orbitResolver = new EventOrbitResolver(_orbit);
        _orbit.OrbitChanged += () => _eventBus?.EmitOrbitChanged();

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

            // 主角區：角色 / HP / 角色卡所在槽變更 → 推主角資料 + 重繪角色卡所在格
            mainMap.CharacterChangedExt += () =>
            {
                if (mainMap.WorldMap.Character is not Character c) return;
                var total = mainMap.GetHeroTotalStats();
                leftPanel.OnCharacterChanged(
                    c,
                    mainMap.WorldMap.Hp,
                    mainMap.WorldMap.HpMax,
                    total,
                    mainMap.WorldMap.CharacterCardSlot);
                // 角色卡格也要在裝備格內顯示
                leftPanel.OnEquipmentChanged(
                    mainMap.WorldMap.Equipped,
                    mainMap.WorldMap.Backpack,
                    mainMap.WorldMap.EquipmentCatalog);
            };

            mainMap.CompanionChangedExt += () => leftPanel.OnCompanionChanged(
                mainMap.WorldMap.Companion,
                mainMap.WorldMap.CompanionHp,
                mainMap.WorldMap.CompanionHpMax);

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
            // Task 9：把 Core EventOrbit 注入到 RightPanel 內建的 OrbitPanel
            _orbitPanel = rightPanel.OrbitPanel;
            _orbitPanel?.SetOrbit(_orbit!);
            if (_orbitPanel is not null)
                _orbitPanel.EventCardClicked += OnOrbitCardClicked;
        }

        // === Task 9 · 事件結算對話框（單例，重複開）===
        _eventDialog = new EventResolutionDialog { Name = "EventResolutionDialog" };
        AddChild(_eventDialog);
        _eventDialog.EventResolved += OnEventResolved;

        // === Task 9 Part A · 玩家移動 → 隨機晉升 1 張 ClassC → ClassA（模擬 tile-enter 觸發）===
        // mainMap 在更上方已 null 檢查並 return，此處保證非 null
        mainMap.PlayerPositionChanged += (_, _) => PromoteRandomEventToClassA();

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

        // === Task 11 Stage 1：載入模組 + 建 GameState（runtime 唯一一次模組載入）===
        LoadModuleAndCreateGameState();

        // === Task 11 Stage 3.4：切到 state-backed WorldMap ===
        // 自此 mainMap.WorldMap 是 GameState 投影；後續 LoadActionDeck / LoadCharacterAndCompanions
        // 等 mutation 走 state.CurrentPlayer.* 寫入路徑（dispatch 已在 Stage 3.1+3.2 完成）。
        if (_module is not null && _gameState is not null)
        {
            mainMap.SwapWorldMap(new CardNarrative.Core.Map.WorldMap(_gameState, _module));
            GD.Print("[MainBootstrap] WorldMap 已切換為 state-backed（GameState 為唯一 SoT）。");
        }

        // === 載入 abandoned-mansion 模組行動卡 / 角色 / 裝備（用 _module）===
        TryLoadAbandonedMansionDeck(mainMap);

        // === Task 9 demo seed：把模組前幾張事件推進 ORBIT，讓 panel 有內容可看（用 _module）===
        TrySeedOrbitDemoEvents();

        GD.Print("[MainBootstrap] 主場景就緒，所有 Signal 已連線。");
    }

    /// <summary>
    /// Task 11 Stage 1：載入 abandoned-mansion 模組 + 建 GameState（runtime 唯一一次）。
    /// gridSize=9 + startPosition=(4,4) 對齊 Phase 1+2 的 9×9 中心起點；
    /// 起始同伴讀 prologue.startingCompanionIds（fallback：old-priest + hired-bodyguard）。
    /// </summary>
    private void LoadModuleAndCreateGameState()
    {
        try
        {
            var schemasFolder = ProjectSettings.GlobalizePath("res://core/schemas/");
            var modulePath = ProjectSettings.GlobalizePath("res://modules/builtin/abandoned-mansion/");
            var loader = new ModuleLoader(schemasFolder);
            var result = loader.Load(modulePath);
            if (result is ModuleLoadResult.Failure fail)
            {
                GD.PrintErr($"[MainBootstrap] 模組載入失敗：{fail.Errors.Count} 個錯誤");
                foreach (var err in fail.Errors) GD.PrintErr($"  - {err}");
                return;
            }
            if (result is not ModuleLoadResult.Success success) return;
            _module = success.Module;

            // 起始角色：demo scholar（若不存在 fallback 第一個）
            string heroId = _module.Characters.ContainsKey("scholar")
                ? "scholar"
                : _module.Characters.Keys.FirstOrDefault() ?? string.Empty;

            // 起始同伴：讀 prologue.startingCompanionIds，缺時 fallback
            var companionIds = _module.Prologue.StartingCompanionIds.Count > 0
                ? _module.Prologue.StartingCompanionIds.ToList()
                : new List<string> { "old-priest", "hired-bodyguard" };
            // 過濾 module 不存在的 ID
            companionIds = companionIds.Where(_module.NpcCompanions.ContainsKey).ToList();

            _gameState = CardNarrative.Core.State.GameState.CreateNew(
                _module,
                chosenCharacterIds: new[] { heroId },
                chosenCompanionIds: companionIds,
                seed: 1234, // demo deterministic seed
                gridSize: 9,
                startPosition: new CardNarrative.Core.State.Position(4, 4));

            GD.Print($"[MainBootstrap] GameState 建立：起始 ({_gameState.CurrentPlayer.Position.X},{_gameState.CurrentPlayer.Position.Y})、"
                     + $"主角 {heroId}、同伴 [{string.Join(", ", companionIds)}]");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[MainBootstrap] 模組載入例外：{ex.Message}");
        }
    }

    private void TryLoadAbandonedMansionDeck(MainMapRenderer mainMap)
    {
        if (_module is null) return;
        try
        {
            // scholar 起手 8 張作為 demo deck
            var deck = _module.ActionCards.Values
                .Where(c => c.Id.StartsWith("scholar-"))
                .Take(8)
                .ToList();
            if (deck.Count == 0)
                deck = _module.ActionCards.Values.Take(8).ToList();
            mainMap.LoadActionDeck(deck);
            GD.Print($"[MainBootstrap] 載入模組成功：{deck.Count} 張行動卡進入牌堆。");

            // 裝備：丟前 3 件進背包當 demo（規格書 §3.4.3 獲得即入背包）
            var equipmentCatalog = _module.Equipment;
            var initialBackpack = equipmentCatalog.Values.Take(3).Select(e => e.Id).ToList();
            mainMap.LoadEquipmentInventory(equipmentCatalog, initialBackpack);
            GD.Print($"[MainBootstrap] 載入裝備目錄：{equipmentCatalog.Count} 件，初始背包 {initialBackpack.Count} 件。");

            // 角色卡 + 多同伴：用 GameState 已選好的角色 / 同伴 ID
            if (_gameState is null) return;
            var heroId = _gameState.CurrentPlayer.CharacterId;
            Character? hero = _module.Characters.GetValueOrDefault(heroId);
            var companions = _gameState.Companions
                .Select(cs => _module.NpcCompanions.GetValueOrDefault(cs.CompanionId))
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();
            if (hero is not null)
            {
                mainMap.LoadCharacterAndCompanions(hero, companions, _module);
                var companionNames = companions.Count > 0
                    ? string.Join(" / ", companions.Select(c => c.Name))
                    : "（無夥伴）";
                GD.Print($"[MainBootstrap] 載入角色卡：{hero.Name}（HP {hero.HpMax}），夥伴：{companionNames}");
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[MainBootstrap] 角色 / 裝備載入例外：{ex.Message}");
        }
    }

    /// <summary>
    /// Task 9 Part A · 把模組所有事件 register 進 ORBIT 為 ClassC（未揭露），
    /// 預先把 2 張升 ClassA 讓 F5 立即可測試結算對話框；結局卡掛 IsEnding 旗標。
    /// 真正的事件來源 → ORBIT register 改寫（含 EventTrigger → JsonLogic 條件翻譯）留待後續 PR。
    /// </summary>
    private void TrySeedOrbitDemoEvents()
    {
        if (_orbit is null || _module is null) return;
        try
        {
            int idx = 0;
            foreach (var ev in _module.Events.Values)
            {
                // demo：前 2 張預設 ClassA 立即可結算；其餘進 ClassC
                var initialClass = idx < 2 ? EventOrbitClass.ClassA : EventOrbitClass.ClassC;
                _orbit.Push(new EventInstance(ev, initialClass: initialClass));
                idx++;
            }

            // 結局卡（ClassC，金邊；未來條件達成時升 A 觸發結局結算）
            var ending = _module.Endings.Values.FirstOrDefault();
            if (ending is not null)
            {
                var endingAsEvent = new EventCard(
                    Id: $"ending-{ending.Id}",
                    Name: ending.Id,
                    Type: EventType.Special,
                    Tn: 0,
                    Trigger: new TileEnterTrigger("any"),
                    Stat: Stat.Skill,
                    AllowedActionTypes: System.Array.Empty<ActionType>(),
                    Narrative: ending.Narrative,
                    Outcomes: new EventOutcomes(
                        Success: new EventOutcome(ending.Narrative, System.Array.Empty<EffectBase>()),
                        PartialSuccess: new EventOutcome(ending.Narrative, System.Array.Empty<EffectBase>()),
                        Failure: new EventOutcome(ending.Narrative, System.Array.Empty<EffectBase>())));
                _orbit.Push(new EventInstance(endingAsEvent, initialClass: EventOrbitClass.ClassC, isEnding: true));
            }

            GD.Print($"[MainBootstrap] ORBIT 載入 {_orbit.Pending.Count} 個事件（含 1 結局卡）。");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[MainBootstrap] ORBIT seed 失敗：{ex.Message}");
        }
    }

    /// <summary>玩家點擊 ORBIT 卡 → 若是 ClassA 則打開結算對話框（B/C 無反應）。</summary>
    private void OnOrbitCardClicked(string eventId)
    {
        if (_orbit is null || _eventDialog is null) return;
        var inst = _orbit.Pending.FirstOrDefault(i => i.Card.Id == eventId);
        if (inst is null) return;
        if (inst.Class != EventOrbitClass.ClassA)
        {
            GD.Print($"[MainBootstrap] ORBIT '{eventId}' 為 {inst.Class}，尚未可結算。");
            return;
        }
        _eventDialog.Open(inst, _mainMap?.WorldMap.Companion);
    }

    /// <summary>結算完成 → 從 ORBIT 移除；後續接 GameState 後可在此套用 outcome.Effects。</summary>
    private void OnEventResolved(string eventId, int tier)
    {
        if (_orbit is null) return;
        var tierName = tier switch { 0 => "成功", 1 => "部分成功", _ => "失敗" };
        GD.Print($"[MainBootstrap] ORBIT '{eventId}' 結算完成：{tierName}");
        _mainMap?.AppendLog($"事件結算「{eventId}」→ {tierName}");
        _orbit.RemoveById(eventId);
    }

    /// <summary>
    /// Task 9 Part A · 玩家每次移動觸發 — 隨機把 1 張 ClassC 升為 ClassA（模擬 tile-enter 觸發）。
    /// 真正的條件式 reveal/trigger 評估留待後續 PR（接 JsonLogicContextBuilder + 真實 GameState）。
    /// </summary>
    private void PromoteRandomEventToClassA()
    {
        if (_orbit is null) return;
        var classCs = _orbit.Pending.Where(i => i.Class == EventOrbitClass.ClassC && !i.IsEnding).ToList();
        if (classCs.Count == 0) return;
        var pick = classCs[_orbitDemoRng.Next(classCs.Count)];
        _orbit.Promote(pick, EventOrbitClass.ClassA);
        _mainMap?.AppendLog($"ORBIT 揭露：{pick.Card.Name}（已可結算）");
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
