using System;
using System.Collections.Generic;
using System.Linq;
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using Godot;
using Projection = CardNarrative.Core.Map.Projection;
using ProjectionParams = CardNarrative.Core.Map.ProjectionParams;
using HauntedManor.Scripts.Ui;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Map;

/// <summary>
/// Phase 1 Task 2/4/5/6 — 9×9 主地圖渲染器（規格書 §2.1 / §5.1）。
/// UI v2 重構：HUD 拆出 → 對外發 Signal 給 TopBar / RightPanel 訂閱。
/// 自身只負責：投影渲染、popup、確認對話框、tile 點擊。
/// </summary>
public partial class MainMapRenderer : Control
{
    [Export] public PackedScene? TileVisualScene { get; set; }

    private readonly WorldMap _worldMap = new();
    private readonly TileVisual[,] _tileNodes = new TileVisual[WorldMap.Size, WorldMap.Size];
    private Node2D? _tileLayer;
    private ProjectionParams _projection;
    private ParallaxSceneController? _parallaxScene; // 任務 8 場景立繪

    // Popup / dialog
    private ActionTriggerPopup? _popup;
    private ConfirmationDialog? _moveConfirm;
    private (int Row, int Col)? _pendingMoveTarget;

    // Demo Skill 屬性（規格書 §3.3 觀察用 = 綠探索）
    private const int DemoSkill = 3;
    private readonly IDiceService _dice = new SeededDiceService(seed: Random.Shared.Next());
    private readonly DiceServiceRollProvider _rollProvider;

    public WorldMap WorldMap => _worldMap;

    public MainMapRenderer()
    {
        _rollProvider = new DiceServiceRollProvider(_dice);
    }

    // === 對外 Signals（取代內嵌 HUD）===

    /// <summary>牌堆狀態變更：發送 (剩餘張數, 持有, NEXT[0], NEXT[1])。空字串代表 -。</summary>
    [Signal] public delegate void DeckStatusChangedEventHandler(int remaining, string heldTerrain, string previewTop, string previewSecond);

    /// <summary>互動模式變更（中文標籤）。</summary>
    [Signal] public delegate void ModeChangedExtEventHandler(string modeLabel);

    /// <summary>HP 變更。</summary>
    [Signal] public delegate void HpChangedExtEventHandler(int hp, int hpMax);

    /// <summary>玩家移動完成（用於 TopBar 顯示位置 / RightPanel 收 log）。</summary>
    [Signal] public delegate void PlayerPositionChangedEventHandler(int row, int col);

    /// <summary>TURN LOG 新增一行。</summary>
    [Signal] public delegate void LogAppendedEventHandler(string line);

    /// <summary>回合變更（NEXT TURN 後）。</summary>
    [Signal] public delegate void TurnChangedExtEventHandler(int turn, int turnLimit);

    /// <summary>AP 變更（行動消耗或 Draw 重置）。</summary>
    [Signal] public delegate void ApChangedExtEventHandler(int ap, int apMax);

    /// <summary>手牌數變更（demo 計數）。</summary>
    [Signal] public delegate void HandChangedExtEventHandler(int hand, int handMax);

    /// <summary>手牌實際內容變更：傳出每張卡的 (id, name, type, cost) 序列化字串。</summary>
    [Signal] public delegate void HandCardsChangedEventHandler(string[] cardIds, string[] cardNames, string[] cardTypes, int[] cardCosts);

    /// <summary>同伴代消耗發生（log 用）。</summary>
    [Signal] public delegate void CompanionApSubstitutedEventHandler(string companionDisplayName);

    /// <summary>行動卡抽牌堆 / 棄牌堆計數變更。</summary>
    [Signal] public delegate void ActionDeckCountsChangedEventHandler(int drawCount, int discardCount);

    /// <summary>裝備配置變更（任何 slot 或 backpack 變動）。LeftPanel 訂閱以重繪 3×3。</summary>
    [Signal] public delegate void EquipmentChangedExtEventHandler();

    public override void _Ready()
    {
        _tileLayer = GetNode<Node2D>("TileLayer");
        var w = (float)Size.X;
        var h = (float)Size.Y;
        _projection = MakeProjection(w, h);

        TileVisualScene ??= ResourceLoader.Load<PackedScene>("res://scenes/map/tile_visual.tscn");

        // Popup + dialog
        _popup = GetNodeOrNull<ActionTriggerPopup>("ActionTriggerPopup");
        _moveConfirm = GetNodeOrNull<ConfirmationDialog>("MoveConfirmDialog");

        if (_popup != null)
        {
            _popup.MoveSelected += OnMoveSelected;
            _popup.RestSelected += OnRestSelected;
            _popup.ObserveSelected += OnObserveSelected;
            _popup.TalkSelected += OnTalkSelected;
        }
        if (_moveConfirm != null)
        {
            // ConfirmationDialog 是獨立 Window，不繼承父 Control 的 Theme → 顯式套用
            _moveConfirm.Theme = UiTheme.Build();
            // 移除 Godot 預設標題列（標題會塞進 dialog_text 開頭，整段都在金邊框內）
            _moveConfirm.Borderless = true;
            _moveConfirm.Title = "";
            // OK 按鈕（確認移動）→ Primary 紅底白字
            UiTheme.ApplyPrimaryButtonStyle(_moveConfirm.GetOkButton());
            _moveConfirm.Confirmed += OnMoveConfirmed;
            _moveConfirm.Canceled += OnMoveCanceled;
        }

        SpawnAllTiles();
        SubscribeWorldMap();

        var resetBtn = GetNodeOrNull<Button>("ResetButtonContainer/ResetButton");
        if (resetBtn != null) resetBtn.Pressed += OnResetPressed;

        // 動態加 PlayCardZone（規格書 §4.2.2 拖曳出牌接收區）
        var dropZone = new PlayCardZone { Name = "PlayCardZone" };
        AddChild(dropZone);
        // _Ready 內部會 SetAnchorsAndOffsetsPreset(FullRect) — 自動覆蓋整個 MapArea
        dropZone.CardDropped += OnCardDroppedToMap;
        // 把 PlayCardZone 移到 TileLayer 之後、UI Controls (Reset/Popup/Dialog) 之前
        // 避免 PlayCardZone 蓋住 popup 與 reset button
        var tileLayer = GetNodeOrNull("TileLayer");
        if (tileLayer != null)
        {
            MoveChild(dropZone, tileLayer.GetIndex() + 1);
        }

        // 任務 8：場景立繪（漂浮在玩家地塊上方）
        _parallaxScene = new ParallaxSceneController { Name = "ParallaxScene" };
        AddChild(_parallaxScene);
        _parallaxScene.ZIndex = 5; // 蓋過 tile，但低於 popup/dialog

        // 視框尺寸可能還是 0（若被父 container 排版尚未完成）→ 等下一個 frame 重算
        CallDeferred(nameof(InitialLayout));
    }

    private void OnCardDroppedToMap(string cardId)
    {
        RequestPlayCard(cardId);
    }

    private void InitialLayout()
    {
        var w = (float)Size.X;
        var h = (float)Size.Y;
        if (w > 0 && h > 0) _projection = MakeProjection(w, h);
        UpdateAllTiles();
        EmitHudSignals();
        AppendLog($"歡迎來到廢棄洋房。玩家位於 ({_worldMap.PlayerPos.Row},{_worldMap.PlayerPos.Col})。");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            var w = (float)Size.X;
            var h = (float)Size.Y;
            if (w > 0 && h > 0)
            {
                _projection = MakeProjection(w, h);
                if (_tileLayer != null && _tileNodes[0, 0] != null) UpdateAllTiles();
            }
        }
    }

    /// <summary>
    /// 產生 7×7 視野 + 強透視（FarScale=0.55）+ 幾何尺度 Y 的投影參數。
    /// 同欄列邊緣呈直線 + 每列 1:1：
    ///   yRange = BaseTileSize × (1-FarScale) / K
    ///   K = (1/FarScale)^δ - (1/FarScale)^(-δ)，δ = 0.5/visibleRows
    ///   FarScale=0.55, visibleRows=7 → yRatio ≈ 5.27
    /// </summary>
    private ProjectionParams MakeProjection(float w, float h)
    {
        const float yRatio = 5.27f; // 幾何尺度 + 7 列 + FarScale=0.55 下的 1:1 條件
        var idealTile = w / 7.0f;
        var idealY = idealTile * yRatio;
        var yRange = Mathf.Min(idealY, h);
        var baseTile = idealY > h ? yRange / yRatio : idealTile;
        return Projection.Default with
        {
            ViewWidth = w,
            ViewHeight = h,
            BaseTileSize = baseTile,
            VanishingPointY = h - yRange,
            GroundY = h,
        };
    }

    public override void _ExitTree()
    {
        UnsubscribeWorldMap();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mb) return;

        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (_popup is { Visible: true } popup)
            {
                var popupRect = new Rect2(popup.Position, popup.Size);
                if (!popupRect.HasPoint(mb.Position))
                {
                    popup.HidePopup();
                    AcceptEvent();
                    return;
                }
                return;
            }

            var tile = FindClickedTile(mb.Position);
            if (tile.HasValue)
            {
                OnTileClicked(tile.Value.row, tile.Value.col);
                AcceptEvent();
            }
        }
        else if (mb.ButtonIndex == MouseButton.Right)
        {
            if (_worldMap.Mode == InteractionMode.Move)
            {
                _worldMap.CancelMoveMode();
                AppendLog("已取消移動。");
                AcceptEvent();
            }
            else if (_worldMap.Mode == InteractionMode.MapExpand)
            {
                _worldMap.CancelMapExpand();
                AppendLog("已取消放置（地塊放回牌堆）。");
                AcceptEvent();
            }
        }
    }

    private (int row, int col)? FindClickedTile(Vector2 localPos)
    {
        var (playerRow, playerCol) = _worldMap.PlayerPos;
        var (offsetRow, offsetCol) = _worldMap.CameraOffset;
        float bestDistSq = float.MaxValue;
        (int row, int col)? best = null;

        for (int r = 0; r < WorldMap.Size; r++)
        for (int c = 0; c < WorldMap.Size; c++)
        {
            var relRow = (r - playerRow) - (int)Mathf.Round(offsetRow);
            var relCol = (c - playerCol) - (int)Mathf.Round(offsetCol);
            if (!Projection.IsVisible(relRow, relCol, _projection)) continue;

            var quad = Projection.ProjectQuad(relRow, relCol, _projection);
            var poly = new Vector2[]
            {
                new(quad.BackLeft.X, quad.BackLeft.Y),
                new(quad.BackRight.X, quad.BackRight.Y),
                new(quad.FrontRight.X, quad.FrontRight.Y),
                new(quad.FrontLeft.X, quad.FrontLeft.Y),
            };
            if (!Geometry2D.IsPointInPolygon(localPos, poly)) continue;

            var dx = localPos.X - quad.Center.X;
            var dy = localPos.Y - quad.Center.Y;
            var dSq = dx * dx + dy * dy;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = (r, c);
            }
        }
        return best;
    }

    // === 公開 API（給 TopBar 等外部呼叫）===

    /// <summary>外部觸發抽下一張地塊（取代原本內嵌 HUD 按鈕）。</summary>
    public void RequestDrawTile()
    {
        if (_worldMap.Mode != InteractionMode.Idle)
        {
            AppendLog("目前不是待命狀態，無法抽地塊。");
            return;
        }
        if (_worldMap.RemainingTiles == 0)
        {
            AppendLog("牌堆已空。");
            return;
        }
        _worldMap.BeginMapExpand();
        AppendLog($"抽到地塊：{_worldMap.HeldTile}，請點擊綠色合法區放置。");
    }

    /// <summary>外部注入行動卡 deck（規格書 §3.4 序幕 setupRules.initialActionDeck）。</summary>
    public void LoadActionDeck(IEnumerable<ActionCard> cards)
    {
        _worldMap.LoadActionDeck(cards);
        AppendLog($"載入行動卡牌堆（{_worldMap.Hand.Count} 張在手牌、{_worldMap.ActionDeckRemaining} 張在抽牌堆）");
    }

    /// <summary>外部觸發出牌（HandDock 點卡 → 確認對話框 → 此處）。</summary>
    public void RequestPlayCard(string cardId)
    {
        var result = _worldMap.TryPlayCard(cardId);
        if (result.Success)
        {
            AppendLog($"打出「{cardId}」消耗 {result.ApSpent} AP（手牌 {_worldMap.HandSize}/{WorldMap.HandSizeMax}、棄牌堆 {_worldMap.ActionDiscardCount}）");
        }
        else
        {
            AppendLog($"出牌失敗：{result.Message}");
        }
    }

    /// <summary>外部注入裝備目錄與初始背包內容（規格書 §3.4.3）。</summary>
    public void LoadEquipmentInventory(
        IReadOnlyDictionary<string, Equipment> catalog,
        IEnumerable<string> initialBackpack)
    {
        _worldMap.LoadEquipmentInventory(catalog, initialBackpack);
        AppendLog($"載入裝備目錄（{catalog.Count} 件，背包 {_worldMap.Backpack.Count}/{EquipmentManager.BackpackMax}）");
    }

    /// <summary>外部觸發裝備移動（LeftPanel drag-drop → 此處）。</summary>
    public void RequestMoveEquipment(string srcKind, int srcIdx, string tgtKind, int tgtIdx)
    {
        MoveEquipmentResult result;
        if (srcKind == "primary" && tgtKind == "primary")
        {
            result = _worldMap.MoveEquipmentSlotToSlot((EquipmentSlot)srcIdx, (EquipmentSlot)tgtIdx);
        }
        else if (srcKind == "primary" && tgtKind == "backpack")
        {
            result = _worldMap.MoveEquipmentSlotToBackpack((EquipmentSlot)srcIdx);
        }
        else if (srcKind == "backpack" && tgtKind == "primary")
        {
            result = _worldMap.MoveEquipmentBackpackToSlot(srcIdx, (EquipmentSlot)tgtIdx);
        }
        else
        {
            // backpack ↔ backpack 純位置調整（不影響邏輯，目前不實作）
            return;
        }

        if (result == MoveEquipmentResult.Ok)
        {
            AppendLog($"裝備移動：{srcKind}#{srcIdx} → {tgtKind}#{tgtIdx}");
        }
        else
        {
            AppendLog($"裝備移動失敗：{result}");
        }
    }

    /// <summary>外部觸發 NEXT TURN — 推進到下一回合（規格書 §3.1.1 簡化版）。</summary>
    public void RequestAdvanceTurn()
    {
        if (_worldMap.Mode != InteractionMode.Idle)
        {
            AppendLog("目前不是待命狀態，無法結束回合。");
            return;
        }
        if (_worldMap.Turn >= WorldMap.TurnLimit)
        {
            AppendLog($"已達 {WorldMap.TurnLimit} 回合上限（後續 Phase 將觸發失敗結局）。");
            return;
        }
        AppendLog($"—— 結束第 {_worldMap.Turn} 回合 ——");
        _worldMap.AdvanceTurn();
        AppendLog($"—— 第 {_worldMap.Turn} 回合 開始（Draw：AP {_worldMap.Ap}/{WorldMap.ApMax}，手牌補至 {_worldMap.HandSize}/{WorldMap.HandSizeMax}）——");
    }

    // === Spawn / subscribe ===

    private void SpawnAllTiles()
    {
        if (_tileLayer is null || TileVisualScene is null)
        {
            GD.PrintErr("[MainMapRenderer] TileLayer 或 TileVisualScene 未設定");
            return;
        }
        for (int r = 0; r < WorldMap.Size; r++)
        for (int c = 0; c < WorldMap.Size; c++)
        {
            var node = TileVisualScene.Instantiate<TileVisual>();
            node.Name = $"Tile_{r}_{c}";
            node.Row = r;
            node.Col = c;
            // TileClicked signal 仍保留作為診斷/備用，主 click 走 _GuiInput
            _tileLayer.AddChild(node);
            _tileNodes[r, c] = node;
        }
    }

    private void SubscribeWorldMap()
    {
        _worldMap.TileChanged += OnTileChanged;
        _worldMap.PlayerMoved += OnPlayerMoved;
        _worldMap.CameraOffsetChanged += UpdateAllTiles;
        _worldMap.ModeChanged += OnModeChanged;
        _worldMap.TilePlaced += OnTilePlaced;
        _worldMap.HpChanged += OnHpChanged;
        _worldMap.TurnChanged += OnTurnChanged;
        _worldMap.ApChanged += OnApChanged;
        _worldMap.HandSizeChanged += OnHandSizeChanged;
        _worldMap.HandChanged += OnHandChanged;
        _worldMap.CompanionSubstituted += OnCompanionSubstituted;
        _worldMap.EquipmentChanged += OnEquipmentChanged;
    }

    private void UnsubscribeWorldMap()
    {
        _worldMap.TileChanged -= OnTileChanged;
        _worldMap.PlayerMoved -= OnPlayerMoved;
        _worldMap.CameraOffsetChanged -= UpdateAllTiles;
        _worldMap.ModeChanged -= OnModeChanged;
        _worldMap.TilePlaced -= OnTilePlaced;
        _worldMap.HpChanged -= OnHpChanged;
        _worldMap.TurnChanged -= OnTurnChanged;
        _worldMap.ApChanged -= OnApChanged;
        _worldMap.HandSizeChanged -= OnHandSizeChanged;
        _worldMap.HandChanged -= OnHandChanged;
        _worldMap.CompanionSubstituted -= OnCompanionSubstituted;
        _worldMap.EquipmentChanged -= OnEquipmentChanged;
    }

    private void OnEquipmentChanged()
    {
        EmitSignal(SignalName.EquipmentChangedExt);
    }

    private void OnHandChanged(IReadOnlyList<ActionCard> hand)
    {
        var ids = hand.Select(c => c.Id).ToArray();
        var names = hand.Select(c => c.Name).ToArray();
        var types = hand.Select(c => c.Type.ToString()).ToArray();
        var costs = hand.Select(c => c.Cost).ToArray();
        EmitSignal(SignalName.HandCardsChanged, ids, names, types, costs);
        // 順便推送抽棄牌堆計數（hand 變動通常伴隨抽棄堆變動）
        EmitSignal(SignalName.ActionDeckCountsChanged, _worldMap.ActionDeckRemaining, _worldMap.ActionDiscardCount);
    }

    private void OnCompanionSubstituted(CompanionAiState c)
    {
        AppendLog($"[同伴 {c.DisplayName} 代消耗 1 AP（剩 {c.RemainingAp} AP）]");
        EmitSignal(SignalName.CompanionApSubstituted, c.DisplayName);
    }

    private void OnTurnChanged(int turn)
    {
        EmitSignal(SignalName.TurnChangedExt, turn, WorldMap.TurnLimit);
    }

    private void OnApChanged(int ap, int apMax)
    {
        EmitSignal(SignalName.ApChangedExt, ap, apMax);
    }

    private void OnHandSizeChanged(int hand, int handMax)
    {
        EmitSignal(SignalName.HandChangedExt, hand, handMax);
    }

    // === WorldMap event handlers ===

    private void OnTileChanged(int row, int col)
    {
        var node = _tileNodes[row, col];
        var data = _worldMap.GetTile(row, col);
        node.SetTile(data.Terrain, data.IsPlaced, data.IsExplored);
    }

    private void OnPlayerMoved(int oldRow, int oldCol, int newRow, int newCol)
    {
        // log 由 AttemptMove 統一處理（含 AP 消耗）→ 此處只更新 UI
        EmitSignal(SignalName.PlayerPositionChanged, newRow, newCol);
        UpdateAllTiles();
    }

    private void OnModeChanged()
    {
        EmitHudSignals();
        UpdateAllTiles();
    }

    private void OnTilePlaced(MapTerrain terrain, int row, int col)
    {
        AppendLog($"放置地塊：{terrain} 於 ({row},{col})");
    }

    private void OnHpChanged(int hp)
    {
        EmitSignal(SignalName.HpChangedExt, hp, _worldMap.HpMax);
    }

    // === Tile click dispatch ===

    private void OnTileClicked(int row, int col)
    {
        switch (_worldMap.Mode)
        {
            case InteractionMode.MapExpand:
                if (_worldMap.IsLegalPlacement(row, col))
                {
                    _worldMap.TryPlaceHeldTile(row, col);
                }
                else
                {
                    AppendLog($"({row},{col}) 不是合法放置區。");
                }
                break;

            case InteractionMode.Move:
                if (_worldMap.IsLegalMoveTarget(row, col))
                {
                    _pendingMoveTarget = (row, col);
                    if (_moveConfirm != null)
                    {
                        // AP 預估提示
                        int apCost = _worldMap.FirstMoveUsedThisTurn ? 1 : 0;
                        var costNote = apCost == 0 ? "免費（本回合首次移動）" : $"{apCost} AP";
                        _moveConfirm.DialogText = $"【 確認移動 】\n\n移動到地塊 ({row}, {col}) ？\n消耗：{costNote}";
                        _moveConfirm.PopupCentered();
                    }
                    else
                    {
                        AttemptMove(row, col);
                    }
                }
                else
                {
                    AppendLog($"({row},{col}) 不可移動（需 4 方向相鄰已放格）。");
                }
                break;

            case InteractionMode.Idle:
                if ((row, col) == _worldMap.PlayerPos)
                {
                    ShowPopupAtPlayer();
                }
                break;
        }
    }

    private void ShowPopupAtPlayer()
    {
        if (_popup is null) return;
        var (pr, pc) = _worldMap.PlayerPos;
        var node = _tileNodes[pr, pc];
        _popup.ShowAt(node.Position);
    }

    // === Popup callbacks ===

    private void OnMoveSelected()
    {
        AppendLog("選擇行動：移動 → 點選相鄰已放格。");
        _worldMap.BeginMoveMode();
    }

    private void OnRestSelected()
    {
        if (_worldMap.Ap <= 0)
        {
            AppendLog("AP 為 0，無法休息（休息需消耗 AP 換 HP）。");
            return;
        }
        if (_worldMap.Hp >= _worldMap.HpMax)
        {
            AppendLog("HP 已滿，無法休息。");
            return;
        }
        var result = _worldMap.Rest();
        AppendLog($"休息：消耗 {result.ApSpent} AP → 回 {result.HpGained} HP（{_worldMap.Hp}/{_worldMap.HpMax}）");
    }

    private void OnObserveSelected()
    {
        // AP 不足檢查（首次免費，之後 2 AP）
        int needed = _worldMap.FirstObserveUsedThisTurn ? 2 : 0;
        if (_worldMap.Ap < needed)
        {
            AppendLog($"觀察 AP 不足（需 {needed}，現有 {_worldMap.Ap}）。本回合首次觀察免費，之後每次 2 AP。");
            return;
        }
        var r = _worldMap.Observe(_rollProvider, DemoSkill);
        if (!r.Performed)
        {
            AppendLog("觀察失敗（內部檢查未通過）。");
            return;
        }
        var marker = r.IsDouble6 ? " ★雙6" : r.IsDouble1 ? " ☠雙1" : "";
        var total = r.D1 + r.D2 + r.SkillBonus;
        var costStr = needed == 0 ? "免費" : $"{needed} AP";
        AppendLog(
            $"觀察（{costStr}）：2d6({r.D1 + r.D2})+Skill({r.SkillBonus}) = {total} vs TN={r.Tn} → "
            + (r.Success ? "成功" : "失敗") + marker);
    }

    private void OnTalkSelected() => AppendLog("對話：這個地塊沒有可對話對象。");

    private void OnMoveConfirmed()
    {
        if (_pendingMoveTarget is { } target)
        {
            AttemptMove(target.Row, target.Col);
        }
        _pendingMoveTarget = null;
    }

    private void AttemptMove(int row, int col)
    {
        var (oldRow, oldCol) = _worldMap.PlayerPos;
        var apBefore = _worldMap.Ap;
        var result = _worldMap.TryMovePlayerTo(row, col);
        switch (result)
        {
            case MovePlayerResult.NotEnoughAp:
                AppendLog($"AP 不足，無法移動到 ({row},{col})。本回合首次移動免費，之後每格 1 AP。");
                _worldMap.CancelMoveMode();
                break;
            case MovePlayerResult.IllegalTarget:
                AppendLog($"({row},{col}) 非合法移動目標。");
                break;
            case MovePlayerResult.Ok:
                var apCost = apBefore - _worldMap.Ap;
                var costStr = apCost == 0 ? "免費" : $"-{apCost} AP";
                AppendLog($"玩家移動：({oldRow},{oldCol}) → ({row},{col})（{costStr}，剩 {_worldMap.Ap}/{WorldMap.ApMax} AP）");
                break;
        }
    }

    private void OnMoveCanceled()
    {
        _pendingMoveTarget = null;
        _worldMap.CancelMoveMode();
        AppendLog("已取消移動。");
    }

    private void OnResetPressed() => _worldMap.ResetCameraToPlayer();

    // === Layout ===

    /// <summary>地塊間 push-apart gap 比例（推力 = relCol/relRow × tileSize × 此比例）。</summary>
    private const float TileGapRatio = 0.10f;

    private void UpdateAllTiles()
    {
        var (playerRow, playerCol) = _worldMap.PlayerPos;
        var (offsetRow, offsetCol) = _worldMap.CameraOffset;

        for (int r = 0; r < WorldMap.Size; r++)
        for (int c = 0; c < WorldMap.Size; c++)
        {
            var node = _tileNodes[r, c];
            if (node is null) continue;
            var data = _worldMap.GetTile(r, c);

            var relRow = (r - playerRow) - (int)Mathf.Round(offsetRow);
            var relCol = (c - playerCol) - (int)Mathf.Round(offsetCol);

            if (!Projection.IsVisible(relRow, relCol, _projection))
            {
                node.Visible = false;
                continue;
            }

            var quad = Projection.ProjectQuad(relRow, relCol, _projection);
            var center = new Vector2(quad.Center.X, quad.Center.Y);

            // Push-apart：依 (relCol, relRow) 方向把 tile 推離原位，產生鄰塊間 gap
            // 推力大小用該深度的 tile 寬 / 高度（解法 B：比例與 tile size 一致）
            var tileWidthAtDepth = (quad.FrontRight.X - quad.FrontLeft.X);
            var tileHeightAtDepth = ((quad.FrontLeft.Y + quad.FrontRight.Y) - (quad.BackLeft.Y + quad.BackRight.Y)) * 0.5f;
            var pushOffset = new Vector2(
                relCol * tileWidthAtDepth * TileGapRatio,
                relRow * tileHeightAtDepth * TileGapRatio);

            node.Visible = true;
            node.Position = center + pushOffset;
            node.SetTile(data.Terrain, data.IsPlaced, data.IsExplored);
            node.SetTileQuad(
                new Vector2(quad.BackLeft.X, quad.BackLeft.Y) - center,
                new Vector2(quad.BackRight.X, quad.BackRight.Y) - center,
                new Vector2(quad.FrontRight.X, quad.FrontRight.Y) - center,
                new Vector2(quad.FrontLeft.X, quad.FrontLeft.Y) - center);

            TileVisual.OverlayKind overlay = TileVisual.OverlayKind.None;
            if (r == playerRow && c == playerCol)
                overlay = TileVisual.OverlayKind.PlayerMark;
            else if (_worldMap.Mode == InteractionMode.MapExpand && _worldMap.IsLegalPlacement(r, c))
                overlay = TileVisual.OverlayKind.LegalPlacement;
            else if (_worldMap.Mode == InteractionMode.Move && _worldMap.IsLegalMoveTarget(r, c))
                overlay = TileVisual.OverlayKind.MoveTarget;
            node.SetOverlay(overlay);
        }

        UpdateParallaxScene();
    }

    /// <summary>場景立繪：跟隨玩家所在地塊（含 camera offset 與 push-apart offset），floor 與 player tile 對齊。</summary>
    private void UpdateParallaxScene()
    {
        if (_parallaxScene == null) return;

        var (offsetRow, offsetCol) = _worldMap.CameraOffset;
        var playerRelRow = -(int)Mathf.Round(offsetRow);
        var playerRelCol = -(int)Mathf.Round(offsetCol);

        if (!Projection.IsVisible(playerRelRow, playerRelCol, _projection))
        {
            _parallaxScene.Visible = false;
            return;
        }
        _parallaxScene.Visible = true;

        var playerQuad = Projection.ProjectQuad(playerRelRow, playerRelCol, _projection);

        // 與 player tile 同步的 push-apart offset（camera 移動時 tile 會位移，
        // 立繪盒的 4 個 corner 也要套用相同 offset 才能保持對齊）
        var tileWidthAtDepth = playerQuad.FrontRight.X - playerQuad.FrontLeft.X;
        var tileHeightAtDepth = ((playerQuad.FrontLeft.Y + playerQuad.FrontRight.Y)
                              - (playerQuad.BackLeft.Y + playerQuad.BackRight.Y)) * 0.5f;
        var pushOffset = new Vector2(
            playerRelCol * tileWidthAtDepth * TileGapRatio,
            playerRelRow * tileHeightAtDepth * TileGapRatio);

        _parallaxScene.Position = Vector2.Zero;
        _parallaxScene.SetTileQuad(
            new Vector2(playerQuad.BackLeft.X, playerQuad.BackLeft.Y) + pushOffset,
            new Vector2(playerQuad.BackRight.X, playerQuad.BackRight.Y) + pushOffset,
            new Vector2(playerQuad.FrontRight.X, playerQuad.FrontRight.Y) + pushOffset,
            new Vector2(playerQuad.FrontLeft.X, playerQuad.FrontLeft.Y) + pushOffset);

        var data = _worldMap.GetTile(_worldMap.PlayerPos.Row, _worldMap.PlayerPos.Col);
        _parallaxScene.SetTerrain(data.Terrain);
    }

    private void EmitHudSignals()
    {
        var preview = _worldMap.NextTilePreview;
        var top = preview.Count > 0 ? preview[0].ToString() : "";
        var second = preview.Count > 1 ? preview[1].ToString() : "";
        EmitSignal(
            SignalName.DeckStatusChanged,
            _worldMap.RemainingTiles,
            _worldMap.HeldTile?.ToString() ?? "",
            top, second);

        var modeLabel = _worldMap.Mode switch
        {
            InteractionMode.Idle => "待命",
            InteractionMode.MapExpand => "放置地塊",
            InteractionMode.Move => "選擇移動目標",
            _ => "?",
        };
        EmitSignal(SignalName.ModeChangedExt, modeLabel);
        EmitSignal(SignalName.HpChangedExt, _worldMap.Hp, _worldMap.HpMax);
        // Task 6 額外推送
        EmitSignal(SignalName.TurnChangedExt, _worldMap.Turn, WorldMap.TurnLimit);
        EmitSignal(SignalName.ApChangedExt, _worldMap.Ap, WorldMap.ApMax);
        EmitSignal(SignalName.HandChangedExt, _worldMap.HandSize, WorldMap.HandSizeMax);
    }

    private void AppendLog(string line)
    {
        EmitSignal(SignalName.LogAppended, line);
        EmitHudSignals();
    }
}

/// <summary>把 IDiceService 包成 WorldMap.IRollProvider，避免 core/ 直接依賴 SeededDiceService。</summary>
internal sealed class DiceServiceRollProvider : IRollProvider
{
    private readonly IDiceService _dice;
    public DiceServiceRollProvider(IDiceService dice) { _dice = dice; }
    public (int D1, int D2) Roll2d6()
    {
        var r = _dice.Roll2d6();
        return (r.D1, r.D2);
    }
}
