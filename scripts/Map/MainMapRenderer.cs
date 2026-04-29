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

    private WorldMap _worldMap = new();
    private readonly TileVisual[,] _tileNodes = new TileVisual[WorldMap.Size, WorldMap.Size];
    private Node2D? _tileLayer;
    private ProjectionParams _projection;
    private ParallaxSceneController? _parallaxScene; // 任務 8 場景立繪
    private TextureRect? _ghostTile; // MapExpand 模式跟隨滑鼠的 ghost preview
    private CanvasLayer? _ghostLayer; // v1.12：脫離 MainMapRenderer 子層 → 全域 CanvasLayer，跟隨可跨右側面板
    private TileGroupOutlineLayer? _groupOutlineLayer; // v1.12 Stage 7：地塊組金色外框
    private const int GhostSize = 100; // 與 RightPanel TilePreviewCard 同尺寸

    // 攝影機平移動畫：在「rel 座標」層補間（X=row offset, Y=col offset，皆為 tile 單位的小數）。
    // 動畫期間從 (newRow-oldRow, newCol-oldCol) 補間回 Vector2.Zero — 透視會自動依小數深度重投影，
    // 不像「螢幕像素 offset」那樣忽略 perspective 非線性，所以滑動感才會平穩。
    private Vector2 _cameraSubTileOffset = Vector2.Zero;
    private Tween? _cameraTween;
    private const float CameraMoveDurationSec = 0.25f;

    private static readonly System.Collections.Generic.Dictionary<MapTerrain, string> GhostTexturePaths = new()
    {
        { MapTerrain.Forest,   "res://art/tiles/forest.png"   },
        { MapTerrain.Path,     "res://art/tiles/path.png"     },
        { MapTerrain.Grass,    "res://art/tiles/grass.png"    },
        { MapTerrain.Water,    "res://art/tiles/water.png"    },
        { MapTerrain.Mountain, "res://art/tiles/mountain.png" },
        { MapTerrain.Building, "res://art/tiles/building.png" },
    };

    // Popup / dialog
    private ActionTriggerPopup? _popup;
    private ConfirmationDialog? _moveConfirm;
    private (int Row, int Col)? _pendingMoveTarget;
    // Phase 3 移動 UX 改造：多格路徑（從 player 走到 goal，含 goal）+ 規劃服務 + 路徑預覽 overlay
    private System.Collections.Generic.List<CardNarrative.Core.State.Position>? _pendingPath;
    private readonly CardNarrative.Core.Services.MapPathFinding _pathFinding = new();
    private PathPreviewOverlay? _pathPreview;
    private (int Row, int Col)? _hoveredTile;
    /// <summary>v1.10 殘影修正：移動進行中 suppress 預覽重算（OnPlayerMoved / UpdateHoverFromMousePosition / UpdateAllTiles 都尊重）。</summary>
    private bool _previewSuppressed;
    /// <summary>v1.11 兩階段移動：閒置時無追蹤；第一次 click 地塊 → 進入預覽模式（cursor 跟隨）；第二次 click → 走。</summary>
    private bool _previewMode;

    // Demo Skill 屬性（規格書 §3.3 觀察用 = 綠探索）
    private const int DemoSkill = 3;
    private readonly IDiceService _dice = new SeededDiceService(seed: Random.Shared.Next());
    private readonly DiceServiceRollProvider _rollProvider;

    public WorldMap WorldMap => _worldMap;

    /// <summary>
    /// Phase 2 任務 11 Stage 3.4：把當前 WorldMap 換成 state-backed instance。
    /// 由 MainBootstrap._Ready 在建好 GameState + Module 後呼叫。
    /// 流程：unsubscribe 舊 → swap → subscribe 新 → 重接小地圖 → 重新渲染。
    /// </summary>
    public void SwapWorldMap(WorldMap newMap)
    {
        if (ReferenceEquals(_worldMap, newMap)) return;
        UnsubscribeWorldMap();
        _worldMap = newMap;
        SubscribeWorldMap();
        // 重新接小地圖
        var minimapGrid = GetNodeOrNull<MinimapRenderer>("Minimap/Grid");
        minimapGrid?.AttachWorldMap(_worldMap);
        // 重新觸發一次 HUD signals 與 tile 投影
        if (_tileLayer != null) UpdateAllTiles();
        EmitHudSignals();
        EmitSignal(SignalName.CharacterChangedExt);
        EmitSignal(SignalName.CompanionChangedExt);
        EmitSignal(SignalName.EquipmentChangedExt);
    }

    /// <summary>當前載入的模組（給 LeftPanel 計算 4 屬性總值與裝備加值查表用）。</summary>
    public CardNarrative.Core.Models.Module? Module { get; private set; }

    public void LoadCharacterAndCompanion(
        Character character,
        NpcCompanion? companion,
        CardNarrative.Core.Models.Module module)
    {
        // 既有單一同伴 API — 內部包成 list 走 LoadCharacterAndCompanions
        var companions = companion is null
            ? System.Array.Empty<NpcCompanion>()
            : new[] { companion };
        LoadCharacterAndCompanions(character, companions, module);
    }

    /// <summary>
    /// Phase 2 任務 11 Stage 1：多同伴載入。LeftPanel 顯示首位同伴；
    /// 其餘進 <see cref="WorldMap.LoadCompanions"/> AI list 供 AP 代消耗用。
    /// </summary>
    public void LoadCharacterAndCompanions(
        Character character,
        System.Collections.Generic.IReadOnlyList<NpcCompanion> companions,
        CardNarrative.Core.Models.Module module)
    {
        Module = module;
        _worldMap.LoadCharacter(character);
        _worldMap.LoadCompanions(companions);
        // LeftPanel 仍顯示單一同伴卡（首位）— Stage 2/3 完整 façade 後再考慮多同伴 UI
        _worldMap.LoadCompanion(companions.Count > 0 ? companions[0] : null);
    }

    /// <summary>主角總屬性 = 角色 base + 裝備加總（跳過角色卡所在槽）。</summary>
    public StatBlock GetHeroTotalStats()
    {
        var c = _worldMap.Character;
        if (c is null) return new StatBlock(0, 0, 0, 0);
        var bonus = AggregateEquipmentStats();
        return new StatBlock(
            c.Stats.Power + bonus.Power,
            c.Stats.Social + bonus.Social,
            c.Stats.Skill + bonus.Skill,
            c.Stats.Intellect + bonus.Intellect);
    }

    private StatBlock AggregateEquipmentStats()
    {
        int p = 0, s = 0, sk = 0, i = 0;
        foreach (var (slot, id) in _worldMap.Equipped)
        {
            if (slot == _worldMap.CharacterCardSlot) continue;
            if (id is null) continue;
            if (!_worldMap.EquipmentCatalog.TryGetValue(id, out var eq)) continue;
            if (eq.StatBonuses is null) continue;
            p += eq.StatBonuses.Power;
            s += eq.StatBonuses.Social;
            sk += eq.StatBonuses.Skill;
            i += eq.StatBonuses.Intellect;
        }
        return new StatBlock(p, s, sk, i);
    }

    public MainMapRenderer()
    {
        _rollProvider = new DiceServiceRollProvider(_dice);
    }

    // === 對外 Signals（取代內嵌 HUD）===

    /// <summary>
    /// 牌堆狀態變更：發送 (剩餘, 持有 terrain, 持有名, NEXT[0] terrain, NEXT[0] 名, NEXT[1] terrain, NEXT[1] 名, NEXT[2] terrain, NEXT[2] 名)。
    /// 空字串代表 —。terrain 用 MapTerrain enum 字串；name 為 module.Tiles[id].Name（如「村內雜貨店」），standalone / 無 module 時為空。
    /// </summary>
    [Signal] public delegate void DeckStatusChangedEventHandler(
        int remaining,
        string heldTerrain, string heldName,
        string previewTop, string previewTopName,
        string previewSecond, string previewSecondName,
        string previewThird, string previewThirdName);

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

    /// <summary>角色卡 / 角色 HP / 角色卡所在槽位變更。LeftPanel 訂閱重繪主角區 + 角色卡所在格。</summary>
    [Signal] public delegate void CharacterChangedExtEventHandler();

    /// <summary>夥伴卡 / 夥伴 HP 變更。LeftPanel 訂閱重繪夥伴區。</summary>
    [Signal] public delegate void CompanionChangedExtEventHandler();

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

        // Task 10 — 小地圖節點 + 「回到玩家中心」按鈕（取代原 main_map.tscn 內的 ResetButtonContainer）
        var minimapGrid = GetNodeOrNull<MinimapRenderer>("Minimap/Grid");
        if (minimapGrid != null)
        {
            minimapGrid.AttachWorldMap(_worldMap);
            minimapGrid.TileHighlightRequested += OnMinimapTileHighlightRequested;
        }
        var minimapResetBtn = GetNodeOrNull<Button>("Minimap/ResetButton");
        if (minimapResetBtn != null) minimapResetBtn.Pressed += OnResetPressed;

        // 動態加 PlayCardZone（規格書 §4.2.2 拖曳出牌接收區）
        var dropZone = new PlayCardZone { Name = "PlayCardZone" };
        AddChild(dropZone);
        // _Ready 內部會 SetAnchorsAndOffsetsPreset(FullRect) — 自動覆蓋整個 MapArea
        dropZone.CardDropped += OnCardDroppedToMap;
        // 把 PlayCardZone 移到 TileLayer 之後、UI Controls (Minimap/Popup/Dialog) 之前
        // 避免 PlayCardZone 蓋住 popup 與小地圖點擊
        var tileLayer = GetNodeOrNull("TileLayer");
        if (tileLayer != null)
        {
            MoveChild(dropZone, tileLayer.GetIndex() + 1);
        }

        // 任務 8：場景立繪（漂浮在玩家地塊上方）
        _parallaxScene = new ParallaxSceneController { Name = "ParallaxScene" };
        AddChild(_parallaxScene);
        _parallaxScene.ZIndex = 5; // 蓋過 tile，但低於 popup/dialog

        // v1.12：MapExpand ghost 改寄生於頂層 CanvasLayer，避開 MainMapRenderer 父層的 hit-test 邊界
        // → 滑鼠滑進右側面板 / 小地圖時 ghost 仍跟隨；位置由 _Process 持續輪詢全域 mouse 位置更新
        _ghostLayer = new CanvasLayer { Name = "GhostLayer", Layer = 12 };
        AddChild(_ghostLayer);
        _ghostTile = new TextureRect
        {
            Name = "GhostTile",
            CustomMinimumSize = new Vector2(GhostSize, GhostSize),
            Size = new Vector2(GhostSize, GhostSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0.85f),
            ZIndex = 20,
        };
        _ghostLayer.AddChild(_ghostTile);

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

    /// <summary>
    /// v1.11：全 viewport 監聽預覽模式退出條件：
    /// - 左鍵 click 地圖區外 → 退出（地圖區內由 _GuiInput 處理）
    /// - 右鍵 click 任意位置 → 退出（取消快捷鍵）
    /// _GuiInput 只在 Control 範圍內觸發，外部 click 被其他 Control 攔截 → 必須用 _Input 全域監聽。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!_previewMode) return;
        if (@event is not InputEventMouseButton { Pressed: true } mb) return;

        if (mb.ButtonIndex == MouseButton.Right)
        {
            // 右鍵：任意位置退出預覽
            ExitPreviewMode();
            return;
        }

        if (mb.ButtonIndex == MouseButton.Left)
        {
            var globalRect = GetGlobalRect();
            if (!globalRect.HasPoint(mb.GlobalPosition))
            {
                ExitPreviewMode();
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Phase 3 移動 UX：mouse motion → 計算 hover tile，更新路徑預覽 overlay
        // （Control 攔截 mouse event 走 _GuiInput，TileVisual 內 Area2D 收不到 hover signal）
        if (@event is InputEventMouseMotion motionEv)
        {
            // v1.12：ghost 位置由 _Process 全域輪詢更新（脫離 MainMapRenderer hit-test 邊界，可跨右側面板跟隨）
            UpdateHoverFromMousePosition(motionEv.Position);
            return;
        }

        // v1.11：Esc 鍵退出預覽模式
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape } && _previewMode)
        {
            ExitPreviewMode();
            AcceptEvent();
            return;
        }

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
            else if (_worldMap.Mode == InteractionMode.MapExpand)
            {
                // 點擊空白區（非任何地塊）→ 取消放置
                _worldMap.CancelMapExpand();
                AppendLog("已取消放置（點擊空白區）。");
                AcceptEvent();
            }
            else if (_previewMode)
            {
                // v1.11：預覽模式中點空白區 → 退出
                ExitPreviewMode();
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
        var (offsetRowInt, offsetRowFrac) = DecomposeOffset(offsetRow);
        var (offsetColInt, offsetColFrac) = DecomposeOffset(offsetCol);
        float bestDistSq = float.MaxValue;
        (int row, int col)? best = null;

        for (int r = 0; r < WorldMap.Size; r++)
        for (int c = 0; c < WorldMap.Size; c++)
        {
            // Visibility check 用整格版（IsVisible 接 int），實際命中測試用 sub-pixel 版確保
            // 拖曳到一半時點擊可命中視覺上實際顯示的 tile。
            var relRowInt = (r - playerRow) - (int)offsetRowInt;
            var relColInt = (c - playerCol) - (int)offsetColInt;
            if (!Projection.IsVisible(relRowInt, relColInt, _projection)) continue;
            var relRow = relRowInt - offsetRowFrac;
            var relCol = relColInt - offsetColFrac;

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
        if (_worldMap.RemainingTiles == 0 && _worldMap.TileChoiceBatchIds.Count == 0)
        {
            AppendLog("牌堆已空。");
            return;
        }
        if (!_worldMap.BeginMapExpand())
        {
            AppendLog("無法抽地塊（牌堆空或狀態不符）。");
            return;
        }
        // v1.12 Stage 5 — 進 MapExpand 模式後 batch 已填，玩家從 RightPanel slot 點選 1 張
        AppendLog($"抽到 {_worldMap.TileChoiceBatchIds.Count} 張，請從右側 RightPanel 三 slot 選一張。");
    }

    /// <summary>
    /// v1.12 Stage 5 — 玩家點 RightPanel batch slot（visual idx 0/1/2）→ 直接傳給 WorldMap.SelectFromBatch。
    /// 後端 SelectFromBatch 自行處理視覺 slot 投影（持有時自己 slot 點到回 false；其他 slot swap）。
    /// </summary>
    public void RequestSelectFromBatch(int slotIdx)
    {
        if (_worldMap.Mode != InteractionMode.MapExpand) return;
        if (!_worldMap.SelectFromBatch(slotIdx))
        {
            // 點到自己的 slot 或越界
            return;
        }
        // v1.12 Stage 6：地塊組額外標示總格數
        var state = _worldMap.BackingState;
        if (state is not null && state.PendingGroupRemaining > 0)
        {
            AppendLog($"持有：{_worldMap.HeldTileId}（地塊組 1/{state.PendingGroupRemaining}），請連續放 {state.PendingGroupRemaining} 格。");
        }
        else
        {
            AppendLog($"持有：{_worldMap.HeldTileId}，請點主地圖綠框合法格放置。");
        }
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

    /// <summary>外部觸發裝備移動（LeftPanel drag-drop → 此處）。支援角色卡來源 srcKind="character"。</summary>
    public void RequestMoveEquipment(string srcKind, int srcIdx, string tgtKind, int tgtIdx)
    {
        MoveEquipmentResult result;
        if (srcKind == "character")
        {
            if (tgtKind != "primary")
            {
                AppendLog("角色卡只能放在主槽，不可入背包。");
                return;
            }
            result = _worldMap.MoveCharacterCardToSlot((EquipmentSlot)tgtIdx);
        }
        else if (srcKind == "primary" && tgtKind == "primary")
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
            // hover 也走 _GuiInput（Control mouse chain 攔截 Area2D mouse event）
            _tileLayer.AddChild(node);
            _tileNodes[r, c] = node;
        }

        // 路徑預覽 overlay：加在 _tileLayer 之上，z-index 在 ParallaxScene 立繪盒之下但高於 tile
        _pathPreview = new PathPreviewOverlay { Name = "PathPreviewOverlay" };
        _tileLayer.AddChild(_pathPreview);

        // v1.12 Stage 7：地塊組金色外框（z-index 高於 tile，低於 path preview）
        _groupOutlineLayer = new TileGroupOutlineLayer
        {
            Name = "TileGroupOutlineLayer",
            ZIndex = 50, // path preview ZIndex=100；tile 預設 0；外框介於中間
            ZAsRelative = false,
        };
        _tileLayer.AddChild(_groupOutlineLayer);
        _groupOutlineLayer.Configure(_worldMap, _tileNodes);
    }

    private void SubscribeWorldMap()
    {
        _worldMap.TileChanged += OnTileChanged;
        _worldMap.TileTransformed += OnTileTransformed;
        _worldMap.PlayerMoved += OnPlayerMoved;
        _worldMap.CameraOffsetChanged += OnWorldCameraOffsetChanged;
        _worldMap.ModeChanged += OnModeChanged;
        _worldMap.TilePlaced += OnTilePlaced;
        _worldMap.HpChanged += OnHpChanged;
        _worldMap.TurnChanged += OnTurnChanged;
        _worldMap.ApChanged += OnApChanged;
        _worldMap.HandSizeChanged += OnHandSizeChanged;
        _worldMap.HandChanged += OnHandChanged;
        _worldMap.CompanionSubstituted += OnCompanionSubstituted;
        _worldMap.EquipmentChanged += OnEquipmentChanged;
        _worldMap.CharacterChanged += OnCharacterChangedInternal;
        _worldMap.CompanionChanged += OnCompanionChangedInternal;
    }

    private void UnsubscribeWorldMap()
    {
        _worldMap.TileChanged -= OnTileChanged;
        _worldMap.TileTransformed -= OnTileTransformed;
        _worldMap.PlayerMoved -= OnPlayerMoved;
        _worldMap.CameraOffsetChanged -= OnWorldCameraOffsetChanged;
        _worldMap.ModeChanged -= OnModeChanged;
        _worldMap.TilePlaced -= OnTilePlaced;
        _worldMap.HpChanged -= OnHpChanged;
        _worldMap.TurnChanged -= OnTurnChanged;
        _worldMap.ApChanged -= OnApChanged;
        _worldMap.HandSizeChanged -= OnHandSizeChanged;
        _worldMap.HandChanged -= OnHandChanged;
        _worldMap.CompanionSubstituted -= OnCompanionSubstituted;
        _worldMap.EquipmentChanged -= OnEquipmentChanged;
        _worldMap.CharacterChanged -= OnCharacterChangedInternal;
        _worldMap.CompanionChanged -= OnCompanionChangedInternal;
    }

    /// <summary>
    /// Phase 2 任務 11 Stage 4：地塊轉變 → 對應 TileVisual 觸發翻牌動畫。
    /// 一般紋理切換已由 TileChanged → UpdateAllTiles 處理；本 handler 只加視覺特效。
    /// </summary>
    private void OnTileTransformed(int row, int col, MapTerrain oldTerrain, MapTerrain newTerrain)
    {
        if (row < 0 || row >= WorldMap.Size || col < 0 || col >= WorldMap.Size) return;
        var node = _tileNodes[row, col];
        node?.TriggerTransformAnimation();
        AppendLog($"地塊變化：({row},{col}) {oldTerrain} → {newTerrain}");
    }

    private void OnCharacterChangedInternal()
    {
        EmitSignal(SignalName.CharacterChangedExt);
    }

    private void OnCompanionChangedInternal()
    {
        EmitSignal(SignalName.CompanionChangedExt);
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

    /// <summary>
    /// 反查 (row, col) 上的 tile 中文卡名（如「村內雜貨店」），standalone 或未放置回 null。
    /// 來源：WorldMap.GetTileId → BackingModule.Tiles[id].Name。
    /// </summary>
    private string? ResolveTileName(int row, int col)
    {
        var id = _worldMap.GetTileId(row, col);
        if (id is null) return null;
        if (_worldMap.BackingModule is null) return null;
        return _worldMap.BackingModule.Tiles.TryGetValue(id, out var tile) ? tile.Name : null;
    }

    private void OnTileChanged(int row, int col)
    {
        var node = _tileNodes[row, col];
        var data = _worldMap.GetTile(row, col);
        node.SetTile(data.Terrain, data.IsPlaced, data.IsExplored);
        node.SetTileName(ResolveTileName(row, col));

        // Stage 6：若變動的格 == 玩家所在格，同步更新場景立繪 terrain
        // （L3-07：transformTile 後三視角同步 — 主地圖紋理 / 小地圖色塊 / 場景立繪）。
        // ParallaxSceneController.SetTerrain 會 short-circuit 同 terrain 不重繪，呼叫成本低。
        var (playerRow, playerCol) = _worldMap.PlayerPos;
        if (row == playerRow && col == playerCol)
        {
            UpdateParallaxScene();
        }

        // v1.12 Stage 7：tile 變動可能影響地塊組外框（新放/移除/transform）→ 重畫
        _groupOutlineLayer?.QueueRedraw();
    }

    private void OnPlayerMoved(int oldRow, int oldCol, int newRow, int newCol)
    {
        // log 由 AttemptMove 統一處理（含 AP 消耗）→ 此處只更新 UI
        EmitSignal(SignalName.PlayerPositionChanged, newRow, newCol);
        AnimateCameraToPlayer(oldRow, oldCol, newRow, newCol);
        // v1.10：移動進行中 suppressed → 不重算 overlay（避免逐格收縮殘影）
        // 走完後 ExecutePendingPath 末尾會解鎖 + 主動重算
        if (_previewSuppressed) return;
        if (_hoveredTile is { } cur) UpdatePathPreviewForHover(cur.Row, cur.Col);
    }

    /// <summary>
    /// 玩家移動後的攝影機平移動畫：在 rel 座標層補間 — 起始把所有 tile 的 rel 偏移
    /// (newRow-oldRow, newCol-oldCol) 個 tile，視覺上即是「舊版面」；補間至 0 即「新版面」。
    /// 因為 ProjectQuad 接受 float rel，每一 frame 都重新透視投影，遠近列各自依 perspective
    /// 換算位置，所以視覺是平滑的攝影機 pan 而非生硬的螢幕線性位移。
    /// </summary>
    private void AnimateCameraToPlayer(int oldRow, int oldCol, int newRow, int newCol)
    {
        // 取消任何進行中的攝影機 tween，避免快速連續移動時動畫疊加。
        _cameraTween?.Kill();

        // 起始 sub-tile offset：把 NEW 版面的 rel 加上這個量，等同回到 OLD 版面的 rel
        // （因為 relOld = relNew + (newRow - oldRow)；對所有 tile 都成立）。
        var startOffset = new Vector2(newRow - oldRow, newCol - oldCol);

        _cameraSubTileOffset = startOffset;
        UpdateAllTiles();

        _cameraTween = CreateTween();
        _cameraTween.TweenMethod(
                Callable.From<Vector2>(SetCameraSubTileOffset),
                startOffset,
                Vector2.Zero,
                CameraMoveDurationSec)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
    }

    private void SetCameraSubTileOffset(Vector2 v)
    {
        _cameraSubTileOffset = v;
        UpdateAllTiles();
    }

    /// <summary>
    /// CameraOffsetChanged（Reset 鍵 / SetCameraOffset）→ 立即跳；殺掉動畫中 tween，
    /// 把 sub-tile offset 歸零，再走原本 UpdateAllTiles。
    /// </summary>
    private void OnWorldCameraOffsetChanged()
    {
        _cameraTween?.Kill();
        _cameraSubTileOffset = Vector2.Zero;
        UpdateAllTiles();
    }

    private void OnModeChanged()
    {
        EmitHudSignals();
        UpdateAllTiles();
        UpdateGhostTileVisibility();
    }

    private void UpdateGhostTileVisibility()
    {
        if (_ghostTile == null) return;
        // v1.12：唯有「持有」時 ghost 才顯示（待選 batch 階段不顯示，避免空白 ghost 誤導）
        bool show = _worldMap.Mode == InteractionMode.MapExpand && _worldMap.HeldTile is { } held;
        _ghostTile.Visible = show;
        if (show && _worldMap.HeldTile is { } t
            && GhostTexturePaths.TryGetValue(t, out var path))
        {
            _ghostTile.Texture = ResourceLoader.Load<Texture2D>(path);
            // 立即用全域 mouse 位置擺放，避免進 MapExpand 瞬間 ghost 從 (0,0) 跳出
            var globalMouse = GetViewport().GetMousePosition();
            _ghostTile.Position = new Vector2(globalMouse.X - GhostSize / 2f, globalMouse.Y - GhostSize / 2f);
        }
    }

    /// <summary>
    /// v1.12 — ghost 持續跟隨「全域」滑鼠位置（GetViewport().GetMousePosition）；
    /// 因為寄生於頂層 CanvasLayer，可跨主地圖 / 右側面板 / 小地圖等任何區域。
    /// </summary>
    public override void _Process(double delta)
    {
        if (_ghostTile is { Visible: true })
        {
            var globalMouse = GetViewport().GetMousePosition();
            _ghostTile.Position = new Vector2(globalMouse.X - GhostSize / 2f, globalMouse.Y - GhostSize / 2f);
        }
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
                    // v1.12 Stage 6：組進行中 → 顯示剩餘進度
                    var state2 = _worldMap.BackingState;
                    if (state2 is not null && state2.PendingGroupRemaining > 0 && _worldMap.HeldTileId is not null)
                    {
                        var tile = _worldMap.BackingModule?.Tiles[_worldMap.HeldTileId];
                        int total = tile?.GroupCount ?? 1;
                        int placed = total - state2.PendingGroupRemaining;
                        AppendLog($"地塊組進度 {placed}/{total} — 請繼續放下一格（與已放格相鄰）。");
                    }
                }
                else
                {
                    // v1.12 Stage 6：組進行中時誤點 → 不要 rollback（防誤觸）；只記日誌
                    var state3 = _worldMap.BackingState;
                    if (state3 is not null && state3.PendingGroupCells.Count > 0)
                    {
                        AppendLog($"({row},{col}) 不在組相鄰範圍；繼續從同組已放格旁挑格，或按右鍵取消整組。");
                    }
                    else
                    {
                        // 一般單格放置誤點 → 取消（保留既有行為）
                        _worldMap.CancelMapExpand();
                        AppendLog($"已取消放置（點擊 ({row},{col}) 不在合法區）。");
                    }
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
                    // 點玩家自身格 → 開行動選單（觀察 / 對話 / 休息）；同時退出預覽模式
                    if (_previewMode) ExitPreviewMode();
                    ShowPopupAtPlayer();
                }
                else if (!_previewMode)
                {
                    // v1.11 第一次 click 地塊 → 進入預覽模式 + 立即顯示路徑（cursor=pin）
                    EnterPreviewMode(row, col);
                }
                else
                {
                    // v1.11 預覽模式中第二次 click 地塊 → 走（cursor 永遠==click 位置，視為確認）
                    RequestMoveToTile(row, col);
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

    /// <summary>v1.11 — 進入預覽模式（第一次 click 地塊觸發）。立即顯示路徑 + cursor 跟隨。</summary>
    private void EnterPreviewMode(int row, int col)
    {
        _previewMode = true;
        _hoveredTile = (row, col);
        UpdatePathPreviewForHover(row, col);
    }

    /// <summary>v1.11 — 退出預覽模式（點玩家自身 / 點空白 / Esc / 走完移動 觸發）。Clear overlay。</summary>
    private void ExitPreviewMode()
    {
        _previewMode = false;
        _hoveredTile = null;
        _pathPreview?.Clear();
    }

    /// <summary>
    /// Phase 3 — 用滑鼠座標反查當前 hover tile（與 click 用同一 FindClickedTile 邏輯），
    /// 與 _hoveredTile 比對；若變更則更新 overlay。v1.10：移動進行中 suppressed → skip。
    /// v1.11：未進入預覽模式 → skip。
    /// </summary>
    private void UpdateHoverFromMousePosition(Vector2 mousePos)
    {
        if (_previewSuppressed) return;
        // v1.11：未進入預覽模式 → mouse 動不追蹤（idle 畫面乾淨）
        if (!_previewMode) return;
        var tile = FindClickedTile(mousePos);
        if (tile is null)
        {
            if (_hoveredTile is not null)
            {
                _hoveredTile = null;
                _pathPreview?.Clear();
            }
            return;
        }

        var (row, col) = tile.Value;
        if (_hoveredTile is { } cur && cur.Row == row && cur.Col == col) return;
        _hoveredTile = (row, col);
        UpdatePathPreviewForHover(row, col);
    }

    private void UpdatePathPreviewForHover(int row, int col)
    {
        if (_pathPreview is null) return;

        // 玩家自身格不顯示預覽
        if ((row, col) == _worldMap.PlayerPos)
        {
            _pathPreview.Clear();
            return;
        }

        var state = _worldMap.BackingState;
        if (state is null)
        {
            // standalone 模式不支援 BFS 預覽
            _pathPreview.Clear();
            return;
        }

        var (pr, pc) = _worldMap.PlayerPos;
        var start = new CardNarrative.Core.State.Position(pc, pr);
        var goal = new CardNarrative.Core.State.Position(col, row);
        // v1.12 Stage 8：傳 module 讓 BFS 套用 tag 相容檢查
        var path = _pathFinding.FindPath(state, start, goal, _worldMap.BackingModule);
        if (path.Count == 0)
        {
            _pathPreview.Clear();
            return;
        }

        // path Position 是 (X=col, Y=row) → 取對應 _tileNodes[row, col].Position 為 tile center
        var pathCenters = new System.Collections.Generic.List<Godot.Vector2>(path.Count);
        foreach (var step in path)
        {
            int sr = step.Y, sc = step.X;
            if (sr >= 0 && sr < WorldMap.Size && sc >= 0 && sc < WorldMap.Size)
                pathCenters.Add(_tileNodes[sr, sc].Position);
        }
        var playerCenter = _tileNodes[pr, pc].Position;
        bool firstAvailable = !_worldMap.FirstMoveUsedThisTurn;
        _pathPreview.SetPreview(playerCenter, pathCenters, _worldMap.Ap, firstAvailable);
    }

    /// <summary>
    /// Phase 3 移動 UX 改造 — 玩家點任意非玩家格時觸發。BFS 計算最短路徑，
    /// 路徑非空時顯示確認對話框（路徑長 + AP cost + 不足提示）；空則 AppendLog 提示無路。
    /// </summary>
    private void RequestMoveToTile(int row, int col)
    {
        var state = _worldMap.BackingState;
        if (state is null)
        {
            // standalone 模式不支援多格路徑 — fallback 既有單格 IsLegalMoveTarget 邏輯
            if (_worldMap.IsLegalMoveTarget(row, col))
            {
                _pendingMoveTarget = (row, col);
                if (_moveConfirm != null)
                {
                    int apCost = _worldMap.FirstMoveUsedThisTurn ? 1 : 0;
                    _moveConfirm.DialogText = $"【 確認移動 】\n\n移動到 ({row}, {col})？\n消耗：{(apCost == 0 ? "免費" : $"{apCost} AP")}";
                    _moveConfirm.PopupCentered();
                }
                else AttemptMove(row, col);
            }
            return;
        }

        // state-mode：BFS 路徑規劃（TileMap key 是 (X=col, Y=row)）
        // v1.12 Stage 8：傳 module 讓 BFS 套用 tag 相容檢查（跨區強制走橋接 tile）
        var start = state.CurrentPlayer.Position;
        var goal = new CardNarrative.Core.State.Position(col, row);
        var path = _pathFinding.FindPath(state, start, goal, _worldMap.BackingModule);
        if (path.Count == 0)
        {
            AppendLog($"({row},{col}) 無路可達（需四方向相鄰已放置格 + tag 相容 / 經橋接 tile）。");
            return;
        }

        bool firstMoveAvailable = !_worldMap.FirstMoveUsedThisTurn;
        int apCostTotal = CardNarrative.Core.Services.MapPathFinding.CalculateApCost(path.Count, firstMoveAvailable);
        int apHave = _worldMap.Ap;

        // Q2=B：AP 不足直接拒絕，提示最遠可達格
        if (apCostTotal > apHave)
        {
            int farthestStepIdx = ComputeFarthestStepIndex(path.Count, firstMoveAvailable, apHave);
            if (farthestStepIdx < 0)
            {
                AppendLog($"({row},{col}) 距離 {path.Count} 格，需 {apCostTotal} AP；目前 {apHave} AP，無法移動。");
            }
            else
            {
                var farthest = path[farthestStepIdx];
                AppendLog($"({row},{col}) 需 {apCostTotal} AP，當前 {apHave} AP — 最遠可走到 ({farthest.Y},{farthest.X})（第 {farthestStepIdx + 1} 格）。");
            }
            return;
        }

        // Q1=A：AP 充足 → 省略確認對話框，直接逐格走完
        _pendingPath = new System.Collections.Generic.List<CardNarrative.Core.State.Position>(path);
        _pendingMoveTarget = null;
        string costNote = firstMoveAvailable && path.Count >= 1
            ? $"{path.Count} 格（首格免費，{apCostTotal} AP）"
            : $"{path.Count} 格（{apCostTotal} AP）";
        AppendLog($"移動：{costNote} → ({row},{col})");
        // v1.10：移動開始 → 立即清空 overlay + 鎖住，避免逐格收縮殘影
        _previewSuppressed = true;
        _pathPreview?.Clear();
        ExecutePendingPath();
    }

    /// <summary>
    /// Q2 helper：計算 AP 內最遠可達 step 的 index（0-based）。回 -1 = 無 AP 走任何 step。
    /// </summary>
    private static int ComputeFarthestStepIndex(int pathLen, bool firstMoveAvailable, int apHave)
    {
        int cumulative = 0;
        int farthest = -1;
        for (int i = 0; i < pathLen; i++)
        {
            int stepCost = (i == 0 && firstMoveAvailable) ? 0 : 1;
            cumulative += stepCost;
            if (cumulative <= apHave) farthest = i;
            else break;
        }
        return farthest;
    }

    private void OnMoveConfirmed()
    {
        // Phase 3：優先走多格路徑；無路徑則 fallback 既有單格 _pendingMoveTarget
        if (_pendingPath is { Count: > 0 })
        {
            ExecutePendingPath();
        }
        else if (_pendingMoveTarget is { } target)
        {
            AttemptMove(target.Row, target.Col);
        }
        _pendingMoveTarget = null;
        _pendingPath = null;
    }

    /// <summary>
    /// 逐格走完 _pendingPath（每格透過 TryMovePlayerTo 套規格 §1.5 鄰接 + AP 規則）。
    /// Stage A：每格之間插入 150ms delay，多格移動有節奏感（仿格子遊戲標準動畫）。
    /// </summary>
    private async void ExecutePendingPath()
    {
        if (_pendingPath is null || _pendingPath.Count == 0) return;
        var pathCopy = new System.Collections.Generic.List<CardNarrative.Core.State.Position>(_pendingPath);
        _pendingPath = null;

        const double StepDelay = 0.30; // 每格 300ms
        for (int i = 0; i < pathCopy.Count; i++)
        {
            var step = pathCopy[i];
            var result = _worldMap.TryMovePlayerTo(step.Y, step.X);
            if (result != MovePlayerResult.Ok)
            {
                AppendLog($"路徑中斷於 ({step.Y},{step.X})：{result}");
                break;
            }
            // 最後一格不必再等
            if (i < pathCopy.Count - 1)
                await ToSignal(GetTree().CreateTimer(StepDelay), SceneTreeTimer.SignalName.Timeout);
        }

        // v1.11：移動完成 → 解鎖 + 退出預覽模式（無 hover 追蹤、畫面乾淨）
        _previewSuppressed = false;
        ExitPreviewMode();
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
        _pendingPath = null;
        _worldMap.CancelMoveMode();
        AppendLog("已取消移動。");
    }

    private void OnResetPressed() => _worldMap.ResetCameraToPlayer();

    /// <summary>Task 10 — 小地圖點擊任一格 → 主地圖該格短暫脈動高亮（不移動視野）。</summary>
    private void OnMinimapTileHighlightRequested(int row, int col)
    {
        if (row < 0 || row >= WorldMap.Size || col < 0 || col >= WorldMap.Size) return;
        var node = _tileNodes[row, col];
        node?.TriggerPulseHighlight();
    }

    // === Layout ===

    /// <summary>地塊間 push-apart gap 比例（推力 = relCol/relRow × tileSize × 此比例）。</summary>
    private const float TileGapRatio = 0.10f;

    /// <summary>
    /// 把連續攝影機 offset 拆成「整格 + 小數」(規格書 §3.5 sub-pixel 平滑拖曳)。
    /// 整格走原 Round 視窗中心對位邏輯；小數從 rel 中減回，產生連續視覺位移。
    /// FracPart ∈ [-0.5, +0.5]。
    /// </summary>
    private static (float IntPart, float FracPart) DecomposeOffset(float v)
    {
        var i = (float)Mathf.Round(v);
        return (i, v - i);
    }

    private void UpdateAllTiles()
    {
        var (playerRow, playerCol) = _worldMap.PlayerPos;
        var (offsetRow, offsetCol) = _worldMap.CameraOffset;
        var (offsetRowInt, offsetRowFrac) = DecomposeOffset(offsetRow);
        var (offsetColInt, offsetColFrac) = DecomposeOffset(offsetCol);
        // 視野上限（含小數動畫時需多容忍 0.5 避免邊緣 tile pop；sub-pixel 拖曳 frac ∈ [-0.5,+0.5] 也在此範圍內）
        var halfRows = _projection.VisibleRows / 2;
        var halfCols = _projection.VisibleCols / 2;
        const float visibilitySlack = 0.5f;

        for (int r = 0; r < WorldMap.Size; r++)
        for (int c = 0; c < WorldMap.Size; c++)
        {
            var node = _tileNodes[r, c];
            if (node is null) continue;
            var data = _worldMap.GetTile(r, c);

            // 小數 rel：基底（int）+ 整格 camera offset + 小數補間 offset(玩家移動) − 拖曳小數 offset(sub-pixel)
            // 規格書 §3.5：拖曳小數從 rel 中減回，產生連續視覺位移避免整格 snap
            var relRow = (r - playerRow) - offsetRowInt + _cameraSubTileOffset.X - offsetRowFrac;
            var relCol = (c - playerCol) - offsetColInt + _cameraSubTileOffset.Y - offsetColFrac;

            if (relRow < -halfRows - visibilitySlack || relRow > halfRows + visibilitySlack
             || relCol < -halfCols - visibilitySlack || relCol > halfCols + visibilitySlack)
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
            node.SetTileName(ResolveTileName(r, c));
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

        // v1.10：tile layout 變動後（中鍵拖曳 / Reset / camera tween）若 hover preview 仍在 → 重算座標
        if (_hoveredTile is { } cur && !_previewSuppressed)
            UpdatePathPreviewForHover(cur.Row, cur.Col);

        // v1.12 Stage 7：tile quad 重排後重畫地塊組外框
        _groupOutlineLayer?.QueueRedraw();
    }

    /// <summary>場景立繪：跟隨玩家所在地塊（含 camera offset 與 push-apart offset），floor 與 player tile 對齊。</summary>
    private void UpdateParallaxScene()
    {
        if (_parallaxScene == null) return;

        var (offsetRow, offsetCol) = _worldMap.CameraOffset;
        var (offsetRowInt, offsetRowFrac) = DecomposeOffset(offsetRow);
        var (offsetColInt, offsetColFrac) = DecomposeOffset(offsetCol);
        // 立繪盒 anchor = 玩家 tile（rel=0）；攝影機補間時把 sub-tile offset 套上，
        // 拖曳小數 offset 也減進來，與 UpdateAllTiles 同步保持立繪與 player tile 對齊。
        var playerRelRow = -offsetRowInt + _cameraSubTileOffset.X - offsetRowFrac;
        var playerRelCol = -offsetColInt + _cameraSubTileOffset.Y - offsetColFrac;

        var halfRows = _projection.VisibleRows / 2;
        var halfCols = _projection.VisibleCols / 2;
        if (playerRelRow < -halfRows - 0.5f || playerRelRow > halfRows + 0.5f
         || playerRelCol < -halfCols - 0.5f || playerRelCol > halfCols + 0.5f)
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
        var module = _worldMap.BackingModule;
        string ResolveName(string? id) =>
            id is not null && module is not null && module.Tiles.TryGetValue(id, out var t) ? t.Name : "";
        MapTerrain? ResolveTerrain(string? id) =>
            id is not null && module is not null && module.Tiles.TryGetValue(id, out var t)
                ? CardNarrative.Core.Map.TileVisualProfileResolver.ResolveTerrain(t)
                : null;

        // v1.12 Stage 5：批次非空（不論 Mode）即顯示 batch；持有時 held 留在原視覺 slot（virtual slot 投影）。
        // 來源：WorldMap.GetBatchVisualSlots() — 已包含 held 的 virtual slot 投影。
        // 批次空時 fallback 到 TileDeck 預覽。
        string?[] virtualSlots;
        if (_worldMap.TileChoiceBatchIds.Count > 0 || _worldMap.HeldTileId is not null)
        {
            virtualSlots = _worldMap.GetBatchVisualSlots().ToArray();
        }
        else
        {
            var deckPreview = _worldMap.NextTileIdPreview;
            virtualSlots = new string?[3];
            for (int i = 0; i < 3 && i < deckPreview.Count; i++) virtualSlots[i] = deckPreview[i];
        }

        string FormatTerrain(string? id) =>
            id is not null && ResolveTerrain(id) is { } t ? t.ToString() : "";

        var top = FormatTerrain(virtualSlots[0]);
        var second = FormatTerrain(virtualSlots[1]);
        var third = FormatTerrain(virtualSlots[2]);
        var topName = ResolveName(virtualSlots[0]);
        var secondName = ResolveName(virtualSlots[1]);
        var thirdName = ResolveName(virtualSlots[2]);
        var heldName = ResolveName(_worldMap.HeldTileId);

        EmitSignal(
            SignalName.DeckStatusChanged,
            _worldMap.RemainingTiles,
            _worldMap.HeldTile?.ToString() ?? "", heldName,
            top, topName,
            second, secondName,
            third, thirdName);

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

    /// <summary>對外開放：讓 MainBootstrap / 其他 UI 元件（如 ORBIT 結算）也能寫入 TURN LOG。</summary>
    public void AppendLog(string line)
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
