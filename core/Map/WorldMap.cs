using CardNarrative.Core.Cards;
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Map;

/// <summary>
/// Phase 1+2 — 9×9 主地圖資料模型（核心邏輯層，無 Godot 依賴）。
/// 對應規格書 §1.5 / §3.1.2 / §3.1.3 / §3.1.4 / §3.4 / §3.7。
///
/// Phase 2 Task 7 新增：
/// - Hand / ActionDeck / Discard（DeckService 包裝行動卡）
/// - Companions 隊伍（CompanionApService 50% 代消耗）
/// - TryConsumeAp 統一 AP 消耗（移動 / 觀察 / 出牌都走這裡）
/// - LoadActionDeck(IEnumerable<ActionCard>) 從外部注入卡組
/// - TryPlayCard(string cardId) 出牌
/// </summary>
public sealed class WorldMap
{
    public const int Size = 9;
    public const int InitialPlayerRow = 4;
    public const int InitialPlayerCol = 4;
    public const int ApMax = 3;
    public const int HandSizeMax = 5;
    public const int TurnLimit = 30;
    public const int ObserveTn = 10;
    public const int CompanionApMax = 2; // 規格書 §3.7

    private readonly TileData[,] _tiles = new TileData[Size, Size];
    private readonly Queue<MapTerrain> _tileDeck = new(new[]
    {
        MapTerrain.Path, MapTerrain.Forest, MapTerrain.Grass, MapTerrain.Water,
        MapTerrain.Path, MapTerrain.Mountain, MapTerrain.Forest, MapTerrain.Grass,
        MapTerrain.Building, MapTerrain.Path,
    });
    private readonly DeckService<ActionCard> _actionDeck;
    private readonly CompanionApService _companionAp;
    private readonly List<ActionCard> _hand = new();
    private readonly List<CompanionAiState> _companions = new();

    // === Phase 2 任務 11 Stage 2b：dual-mode 後端 ===
    // null = standalone（Phase 1+2 既有測試路徑，內部欄位為 source of truth）
    // non-null = state-backed（runtime façade，GameState 為 source of truth）
    // 詳見 docs/任務11-地塊卡變化-分析.md §4 Stage 2
    private readonly GameState? _state;
    private readonly CardNarrative.Core.Models.Module? _module;
    /// <summary>state-backed 模式時暴露 GameState 參照（Stage 2c+ 與 xUnit 用）。</summary>
    public GameState? BackingState => _state;
    /// <summary>state-backed 模式時暴露 Module 參照。</summary>
    public CardNarrative.Core.Models.Module? BackingModule => _module;

    // === Equipment 狀態（規格書 §3.4.3）===
    // PR-A：standalone 路徑保留內部欄位；state-mode 走 GameState.CurrentPlayer.Equipment / Backpack。
    // _equippedStandalone / _backpackStandalone 僅 standalone 模式（xUnit）使用。
    private readonly Dictionary<EquipmentSlot, string?> _equippedStandalone = new();
    private readonly List<string> _backpackStandalone = new();
    private readonly Dictionary<string, Equipment> _equipmentCatalog = new();

    /// <summary>當前裝備配置 dual-mode dispatch（state-mode → state.CurrentPlayer.Equipment）。</summary>
    private IDictionary<EquipmentSlot, string?> EquippedDict =>
        _state is null ? _equippedStandalone : _state.CurrentPlayer.Equipment;

    /// <summary>當前背包 dual-mode dispatch（state-mode → state.CurrentPlayer.Backpack）。</summary>
    private IList<string> BackpackList =>
        _state is null ? _backpackStandalone : _state.CurrentPlayer.Backpack;

    // === Character / Companion 卡片（驅動 LeftPanel 主角區 + 夥伴區） ===
    private Character? _character;
    private NpcCompanion? _companion;
    private int _companionHpStandalone;

    private (int Row, int Col) _playerPos = (InitialPlayerRow, InitialPlayerCol);
    public (int Row, int Col) PlayerPos
    {
        get
        {
            if (_state is null) return _playerPos;
            var p = _state.CurrentPlayer.Position;
            return (p.Y, p.X); // state X=col, Y=row
        }
        private set
        {
            if (_state is null) _playerPos = value;
            else _state.CurrentPlayer.Position = new Position(value.Col, value.Row);
        }
    }

    public (float Row, float Col) CameraOffset { get; private set; } = (0f, 0f);

    public InteractionMode Mode { get; private set; } = InteractionMode.Idle;

    private MapTerrain? _heldTile;
    /// <summary>
    /// state-mode 從 HeldTileId 派生 MapTerrain（透過 TileVisualProfileResolver）；
    /// standalone 使用 _heldTile 內部欄位。
    /// </summary>
    public MapTerrain? HeldTile
    {
        get
        {
            if (_state is null) return _heldTile;
            var heldId = _state.CurrentPlayer.HeldTileId;
            if (heldId is null || _module is null) return null;
            if (!_module.Tiles.TryGetValue(heldId, out var tile)) return null;
            return TileVisualProfileResolver.ResolveTerrain(tile);
        }
        private set
        {
            if (_state is null) _heldTile = value;
            // state-mode：HeldTile 由 state.CurrentPlayer.HeldTileId 決定，外部設值無意義
            // 內部呼叫 BeginMapExpand / TryPlaceHeldTile / CancelMapExpand 時直接操作 state.CurrentPlayer.HeldTileId
        }
    }

    /// <summary>
    /// PR-B · state-mode 當前持有的 tile id（如 "village-store"），dual-mode dispatch 至 state.CurrentPlayer.HeldTileId；
    /// standalone 永遠 null（standalone 無 tile id 概念，僅有 MapTerrain enum）。
    /// UI 可透過此 id 從 <see cref="BackingModule"/>.Tiles[id].Name 取中文卡名。
    /// </summary>
    public string? HeldTileId
    {
        get => _state is null ? null : _state.CurrentPlayer.HeldTileId;
        private set
        {
            if (_state is not null) _state.CurrentPlayer.HeldTileId = value;
            // standalone 無對應欄位（用 _heldTile MapTerrain enum 表示持有狀態）
        }
    }

    /// <summary>
    /// state-mode TileDeck 頂端 3 張 tile id；standalone 永遠空。
    /// 與 <see cref="NextTilePreview"/> 同序對齊（NextTilePreview[i] 對應 NextTileIdPreview[i]）。
    /// </summary>
    public IReadOnlyList<string> NextTileIdPreview
    {
        get
        {
            if (_state is null) return Array.Empty<string>();
            return _state.TileDeck.Take(3).ToList();
        }
    }

    private int _hp = 12;
    public int Hp
    {
        get => _state is null ? _hp : _state.CurrentPlayer.Hp;
        private set
        {
            if (_state is null) _hp = value;
            else _state.CurrentPlayer.Hp = value;
        }
    }

    private int _hpMax = 12;
    public int HpMax
    {
        get => _state is null ? _hpMax : _state.CurrentPlayer.HpMax;
        private set
        {
            // state.CurrentPlayer.HpMax 是 init-only — state 模式下 HpMax 由
            // GameState.CreateNew 一次定值，runtime 不應再改。WorldMap 路徑下若試圖修
            // 視為 no-op（避免破契約），warn 由 caller 端確認。
            if (_state is null) _hpMax = value;
        }
    }

    /// <summary>
    /// 角色卡目前佔住的裝備槽位（規格書 §3.4.3 中央格 → §3-7）。預設 Head。
    /// PR-A：dual-mode dispatch — state-mode 從 state.CurrentPlayer.CharacterCardSlot 派生；
    /// standalone 走 _characterCardSlotStandalone 內部欄位。
    /// </summary>
    public EquipmentSlot CharacterCardSlot
    {
        get => _state is null ? _characterCardSlotStandalone : _state.CurrentPlayer.CharacterCardSlot;
        private set
        {
            if (_state is null) _characterCardSlotStandalone = value;
            else _state.CurrentPlayer.CharacterCardSlot = value;
        }
    }
    private EquipmentSlot _characterCardSlotStandalone = EquipmentSlot.Head;
    public Character? Character => _character;
    public NpcCompanion? Companion => _companion;
    /// <summary>
    /// PR-B · dual-mode dispatch — state-mode 從 state.Companions[0].Hp 派生（首位同伴）；
    /// standalone 走 _companionHpStandalone 內部欄位。state.Companions 為空時 (e.g. 無同伴模組) 回 0。
    /// </summary>
    public int CompanionHp => _state is null
        ? _companionHpStandalone
        : (_state.Companions.Count > 0 ? _state.Companions[0].Hp : 0);
    public int CompanionHpMax => _companion?.Hp ?? 0;

    private int _turn = 1;
    public int Turn
    {
        get => _state is null ? _turn : _state.CurrentBigRound;
        private set
        {
            if (_state is null) _turn = value;
            else _state.CurrentBigRound = value;
        }
    }

    private int _ap = ApMax;
    public int Ap
    {
        get => _state is null ? _ap : _state.CurrentPlayer.ActionPoints;
        private set
        {
            if (_state is null) _ap = value;
            else _state.CurrentPlayer.ActionPoints = value;
        }
    }

    public int HandSize => _state is null ? _hand.Count : _state.CurrentPlayer.Hand.Count;

    private bool _firstMoveUsedThisTurn;
    /// <summary>
    /// 本回合是否已用過首次移動。state-mode 用 PlayerState.MovesThisTurn > 0 派生
    /// （state 沒有等價布林欄位，但 MovesThisTurn 計次提供相同語意）。
    /// </summary>
    public bool FirstMoveUsedThisTurn
    {
        get => _state is null ? _firstMoveUsedThisTurn : _state.CurrentPlayer.MovesThisTurn > 0;
        private set
        {
            if (_state is null) _firstMoveUsedThisTurn = value;
            else
            {
                if (value && _state.CurrentPlayer.MovesThisTurn == 0)
                    _state.CurrentPlayer.MovesThisTurn = 1;
                else if (!value)
                    _state.CurrentPlayer.MovesThisTurn = 0;
            }
        }
    }

    private bool _firstObserveUsedThisTurnStandalone;
    /// <summary>
    /// 本回合是否已用過首次觀察（規格書 §3.1.4 觀察首次免費，之後 2 AP）。
    /// PR-B · dual-mode dispatch — state-mode 用 state.CurrentPlayer.ObservesThisTurn > 0 派生
    /// （與 MovesThisTurn 相同模式）；standalone 走 _firstObserveUsedThisTurnStandalone 內部欄位。
    /// </summary>
    public bool FirstObserveUsedThisTurn
    {
        get => _state is null
            ? _firstObserveUsedThisTurnStandalone
            : _state.CurrentPlayer.ObservesThisTurn > 0;
        private set
        {
            if (_state is null) _firstObserveUsedThisTurnStandalone = value;
            else
            {
                if (value && _state.CurrentPlayer.ObservesThisTurn == 0)
                    _state.CurrentPlayer.ObservesThisTurn = 1;
                else if (!value)
                    _state.CurrentPlayer.ObservesThisTurn = 0;
            }
        }
    }

    /// <summary>
    /// Phase 2 任務 11 Stage 2c：state-mode 從 state.CurrentPlayer.Hand (List&lt;string&gt;) ×
    /// module.ActionCards 投影為 List&lt;ActionCard&gt;；standalone 從內部 _hand 取。
    /// state-mode 為「投影 cache」模式 — 每次 read 重建（Stage 3 引入 invalidate 機制）。
    /// </summary>
    public IReadOnlyList<ActionCard> Hand
    {
        get
        {
            if (_state is null) return _hand;
            return BuildHandFromState();
        }
    }

    private IReadOnlyList<ActionCard> BuildHandFromState()
    {
        if (_module is null) return System.Array.Empty<ActionCard>();
        var hand = _state!.CurrentPlayer.Hand;
        var result = new List<ActionCard>(hand.Count);
        foreach (var cardId in hand)
        {
            if (_module.ActionCards.TryGetValue(cardId, out var card))
                result.Add(card);
        }
        return result;
    }

    /// <summary>state-mode 從 state.CurrentPlayer.Deck.Count 取；standalone 從 _actionDeck.DrawCount。</summary>
    public int ActionDeckRemaining => _state is null
        ? _actionDeck.DrawCount
        : _state.CurrentPlayer.Deck.Count;

    /// <summary>state-mode 從 state.CurrentPlayer.Discard.Count 取；standalone 從 _actionDeck.DiscardCount。</summary>
    public int ActionDiscardCount => _state is null
        ? _actionDeck.DiscardCount
        : _state.CurrentPlayer.Discard.Count;
    public IReadOnlyList<CompanionAiState> Companions => _companions;

    /// <summary>當前裝備配置（slot → equipmentId）。Read-only 對外。PR-A 起 dual-mode dispatch。</summary>
    public IReadOnlyDictionary<EquipmentSlot, string?> Equipped =>
        _state is null
            ? _equippedStandalone
            : (IReadOnlyDictionary<EquipmentSlot, string?>)_state.CurrentPlayer.Equipment;
    /// <summary>背包內容（read-only）。PR-A 起 dual-mode dispatch。</summary>
    public IReadOnlyList<string> Backpack =>
        _state is null ? _backpackStandalone : _state.CurrentPlayer.Backpack;
    /// <summary>裝備目錄（id → Equipment）。catalog 屬 module 級資料，非玩家狀態，仍維持 WorldMap 持有。</summary>
    public IReadOnlyDictionary<string, Equipment> EquipmentCatalog => _equipmentCatalog;

    public IReadOnlyList<MapTerrain> NextTilePreview
    {
        get
        {
            if (_state is null) return _tileDeck.Take(3).ToArray();
            if (_module is null) return System.Array.Empty<MapTerrain>();
            var result = new List<MapTerrain>();
            foreach (var id in _state.TileDeck.Take(3))
            {
                if (_module.Tiles.TryGetValue(id, out var tile))
                    result.Add(TileVisualProfileResolver.ResolveTerrain(tile));
            }
            return result;
        }
    }

    public int RemainingTiles => _state is null ? _tileDeck.Count : _state.TileDeck.Count;

    public event Action<int, int>? TileChanged;
    /// <summary>
    /// Phase 2 任務 11 Stage 4：地塊轉變專屬事件（規格書 §1.5 / §2.4）。
    /// 由 ORBIT outcome 套用 TransformTileEffect 後觸發，攜帶 (row, col, oldTerrain, newTerrain)。
    /// 目的：MainMapRenderer 可訂閱此事件播翻牌 / 閃爍動畫，與一般 TileChanged 區分。
    /// MinimapRenderer / ParallaxScene 仍用 TileChanged 自動覆色 / 切場景，無須特別訂閱本事件。
    /// </summary>
    public event Action<int, int, MapTerrain, MapTerrain>? TileTransformed;
    public event Action<int, int, int, int>? PlayerMoved;
    public event Action? CameraOffsetChanged;
    public event Action? ModeChanged;
    public event Action<MapTerrain, int, int>? TilePlaced;
    public event Action<int>? HpChanged;
    public event Action<int>? TurnChanged;
    public event Action<int, int>? ApChanged;
    public event Action<int, int>? HandSizeChanged;
    public event Action<int, int, int, bool, bool, bool>? ObserveResolved;
    /// <summary>手牌變更（出牌 / Draw 階段補滿）。</summary>
    public event Action<IReadOnlyList<ActionCard>>? HandChanged;
    /// <summary>同伴 AP 變更（代消耗 / Draw 重置）。</summary>
    public event Action? CompanionApChangedEvent;
    /// <summary>AP 代消耗發生：傳代消耗的同伴。</summary>
    public event Action<CompanionAiState>? CompanionSubstituted;

    /// <summary>裝備配置變更（任何 slot 或 backpack 變動）。</summary>
    public event Action? EquipmentChanged;

    /// <summary>角色卡載入 / 切換、角色 HP 變更時觸發；LeftPanel 主角區訂閱。</summary>
    public event Action? CharacterChanged;

    /// <summary>夥伴卡載入 / 切換、夥伴 HP 變更時觸發；LeftPanel 夥伴區訂閱。</summary>
    public event Action? CompanionChanged;

    // === Phase 2 任務 11 Stage 3.3：事件排序層 + 嵌套支援 ===
    // 規格書 §3.4：所有 mutation 完成後 emit event；ORBIT outcome / EffectHandler
    // 套多個 effect 時可用 BeginEventBatch / EndEventBatch 包起來，
    // 內部 emit 會延後到最外層 EndEventBatch 一次依 priority 排序 emit。
    private int _eventDepth;
    private readonly List<(EventPriority Priority, Action Emit)> _eventQueue = new();

    /// <summary>進入事件批次：emit 延後直到對應 EndEventBatch；支援嵌套。</summary>
    public void BeginEventBatch() => _eventDepth++;

    /// <summary>離開事件批次：若到最外層則 flush 排序後 emit。</summary>
    public void EndEventBatch()
    {
        if (_eventDepth <= 0) return;
        _eventDepth--;
        if (_eventDepth == 0) FlushEventBatch();
        if (_eventDepth > 5)
            System.Diagnostics.Debug.WriteLine("[WorldMap] EventDepth > 5 — 可能無限遞迴或 stack 失衡");
    }

    private void FlushEventBatch()
    {
        if (_eventQueue.Count == 0) return;
        // 按 priority 排序 + 簡易去重（同 priority 同 emit hash 視同重複）
        var sorted = _eventQueue.OrderBy(e => (int)e.Priority).ToList();
        _eventQueue.Clear();
        foreach (var (_, emit) in sorted) emit();
    }

    /// <summary>有 batch 進行中則 enqueue；否則直接 emit。</summary>
    private void RaiseOrQueue(EventPriority priority, Action emit)
    {
        if (_eventDepth > 0) _eventQueue.Add((priority, emit));
        else emit();
    }

    // === 公開 Notify API（外部 service 修改 state 後呼叫，觸發 UI 更新）===

    /// <summary>外部 mutation 後通知某格已變更（如 EffectHandler.ApplyTransformTile 後）。</summary>
    public void NotifyTileChanged(int row, int col)
        => RaiseOrQueue(EventPriority.TileChanged, () => TileChanged?.Invoke(row, col));

    /// <summary>
    /// Stage 4：通知某格剛被 transformTile 變化（攜帶舊/新 MapTerrain）。
    /// MainMapRenderer 訂閱此事件觸發翻牌 / 閃爍動畫；
    /// 一般渲染由 TileChanged 處理，本事件不取代之。
    /// 呼叫端應同時呼 NotifyTileChanged（因 priority TileChanged=10 早於 TileTransformed=70，
    /// 紋理先換、再閃爍）。
    /// </summary>
    public void NotifyTileTransformed(int row, int col, MapTerrain oldTerrain, MapTerrain newTerrain)
        => RaiseOrQueue(EventPriority.TileTransformed,
            () => TileTransformed?.Invoke(row, col, oldTerrain, newTerrain));

    /// <summary>外部 mutation 後通知玩家位置變更。</summary>
    public void NotifyPlayerMoved(int oldRow, int oldCol, int newRow, int newCol)
        => RaiseOrQueue(EventPriority.PlayerMoved,
            () => PlayerMoved?.Invoke(oldRow, oldCol, newRow, newCol));

    /// <summary>外部 mutation 後通知 HP 變更。</summary>
    public void NotifyHpChanged(int hp)
        => RaiseOrQueue(EventPriority.HpChanged, () => HpChanged?.Invoke(hp));

    /// <summary>外部 mutation 後通知 AP 變更。</summary>
    public void NotifyApChanged(int ap, int apMax)
        => RaiseOrQueue(EventPriority.ApChanged, () => ApChanged?.Invoke(ap, apMax));

    /// <summary>
    /// PR-A · 外部 mutation 後通知裝備配置 / 背包變更（如 EffectHandler 套 GrantEquipmentEffect 後）。
    /// 走 event batch + priority 排序；UI 訂閱 EquipmentChanged 事件即可重繪 LeftPanel 裝備格 / 背包。
    /// </summary>
    public void NotifyEquipmentChanged()
        => RaiseOrQueue(EventPriority.EquipmentChanged, () => EquipmentChanged?.Invoke());

    /// <summary>
    /// Stage 6 · 外部 mutation 後通知同伴變更（如戰鬥內 BattleEngine 直接 mutate state.Companions[i].Hp 後）。
    /// </summary>
    public void NotifyCompanionChanged()
        => RaiseOrQueue(EventPriority.CompanionChanged, () => CompanionChanged?.Invoke());

    public WorldMap() : this(new SystemRandomProvider()) { }

    /// <summary>
    /// Phase 2 任務 11 Stage 2b：state-backed 模式建構子。
    /// 詳見 docs/任務11-地塊卡變化-分析.md §4 Stage 2 / §9 v3 修訂。
    /// 此模式下 Hp/Ap/Turn/PlayerPos 等 primitive 由 GameState 為唯一 source of truth；
    /// WorldMap 仍保留標準初始化欄位，但 getter 會 dispatch 到 GameState。
    /// </summary>
    public WorldMap(GameState state, CardNarrative.Core.Models.Module module)
        : this(state, module, new SystemRandomProvider()) { }

    /// <summary>state-backed + 自訂 random — 主要供 xUnit 測試使用。</summary>
    public WorldMap(GameState state, CardNarrative.Core.Models.Module module, IRandomProvider random)
        : this(random)
    {
        _state = state;
        _module = module;
    }

    public WorldMap(IRandomProvider random)
    {
        _actionDeck = new DeckService<ActionCard>(random);
        _companionAp = new CompanionApService(random);

        _companions.Add(new CompanionAiState("companion-a", "夥伴 A", CompanionApMax));
        _companions.Add(new CompanionAiState("companion-b", "夥伴 B", CompanionApMax));

        // 裝備 8 個主槽預設為 null（空）— 僅初始化 standalone 路徑；
        // state-mode 由 GameState.CurrentPlayer.Equipment 自然按需建 key（first grant / move）。
        foreach (EquipmentSlot s in new[] {
            EquipmentSlot.Head, EquipmentSlot.Weapon, EquipmentSlot.OffHand,
            EquipmentSlot.Body, EquipmentSlot.AccessoryA, EquipmentSlot.AccessoryB,
            EquipmentSlot.Feet, EquipmentSlot.Utility,
        })
        {
            _equippedStandalone[s] = null;
        }

        for (int r = 0; r < Size; r++)
        for (int c = 0; c < Size; c++)
        {
            _tiles[r, c] = new TileData(r, c, MapTerrain.Forest, IsPlaced: false, IsExplored: false);
        }
        _tiles[InitialPlayerRow, InitialPlayerCol] = new TileData(
            InitialPlayerRow, InitialPlayerCol, MapTerrain.Building,
            IsPlaced: true, IsExplored: true);
    }

    /// <summary>
    /// 從外部（如 ModuleLoader）注入行動卡 deck，並抽至手牌上限。
    /// state-mode 寫進 state.CurrentPlayer.Deck（覆蓋 CreateNew 已洗好的牌組）。
    /// </summary>
    public void LoadActionDeck(IEnumerable<ActionCard> cards)
    {
        if (_state is null)
        {
            _actionDeck.LoadInitial(cards);
            _hand.Clear();
            DrawToHandLimit();
        }
        else
        {
            var ids = cards.Select(c => c.Id).ToList();
            _state.CurrentPlayer.Deck.Clear();
            _state.CurrentPlayer.Deck.AddRange(ids);
            _state.CurrentPlayer.Hand.Clear();
            _state.CurrentPlayer.Discard.Clear();
            DrawToHandLimit();
        }
    }

    /// <summary>
    /// Phase 2 任務 11 Stage 1：從模組真實同伴清單注入 AI state（取代建構子內的 placeholder a/b）。
    /// 規格書 §3.7 同伴 AP 代消耗 — 最多 <see cref="CompanionApMax"/> 個同伴。
    /// </summary>
    public void LoadCompanions(IEnumerable<NpcCompanion> companions)
    {
        _companions.Clear();
        foreach (var c in companions)
        {
            _companions.Add(new CompanionAiState(c.Id, c.Name, CompanionApMax));
            if (_companions.Count >= 2) break; // demo Phase 2 暫定最多 2 同伴
        }
    }

    /// <summary>注入裝備目錄 + 起始背包內容（規格書 §3.4.3 獲得即入背包）。</summary>
    public void LoadEquipmentInventory(
        IReadOnlyDictionary<string, Equipment> catalog,
        IEnumerable<string> initialBackpack)
    {
        _equipmentCatalog.Clear();
        foreach (var (id, eq) in catalog) _equipmentCatalog[id] = eq;
        var backpack = BackpackList;
        backpack.Clear();
        foreach (var id in initialBackpack)
        {
            if (backpack.Count >= EquipmentManager.BackpackMax) break;
            if (!_equipmentCatalog.ContainsKey(id)) continue;
            backpack.Add(id);
        }
        EquipmentChanged?.Invoke();
    }

    /// <summary>載入角色卡：覆蓋 HpMax / Hp / Character；CharacterCardSlot 維持當前值（預設 Head）。</summary>
    public void LoadCharacter(Character character)
    {
        _character = character;
        HpMax = character.HpMax;
        Hp = character.HpMax;
        CharacterChanged?.Invoke();
        HpChanged?.Invoke(Hp);
    }

    /// <summary>
    /// 載入夥伴卡（單一夥伴；多夥伴未來擴充）。
    /// PR-B · state-mode 下若 state.Companions 已含此 companion id 則不覆蓋其 Hp（保留 GameState SoT）；
    /// 否則同步初始 HP。standalone 走 _companionHpStandalone。
    /// </summary>
    public void LoadCompanion(NpcCompanion? companion)
    {
        _companion = companion;
        if (_state is null)
        {
            _companionHpStandalone = companion?.Hp ?? 0;
        }
        else if (companion is not null)
        {
            // state-mode：若 state.Companions[0] 已是同 id，沿用其 Hp（GameState 為 SoT）；
            // 若不同 id 或 Companions 空，補建一筆並用 module 預設 Hp。
            var existing = _state.Companions.FirstOrDefault();
            if (existing is null || existing.CompanionId != companion.Id)
            {
                _state.Companions.Clear();
                _state.Companions.Add(new CompanionState
                {
                    CompanionId = companion.Id,
                    Hp = companion.Hp,
                });
            }
        }
        CompanionChanged?.Invoke();
    }

    /// <summary>角色卡 → 主槽。背包永遠拒絕（由 UI 端阻擋；此處不接背包輸入）。</summary>
    public MoveEquipmentResult MoveCharacterCardToSlot(EquipmentSlot target)
    {
        var (result, newSlot) = EquipmentManager.MoveCharacterCardSlotToSlot(
            EquippedDict, _equipmentCatalog, CharacterCardSlot, target);
        if (result == MoveEquipmentResult.Ok)
        {
            CharacterCardSlot = newSlot;
            EquipmentChanged?.Invoke();
            CharacterChanged?.Invoke();
        }
        return result;
    }

    /// <summary>裝備從另一主槽放進角色卡所在槽：觸發角色卡與該裝備互換。</summary>
    public MoveEquipmentResult SwapCharacterWithEquipmentSlot(EquipmentSlot equipmentSourceSlot)
    {
        var (result, newSlot) = EquipmentManager.SwapCharacterWithEquipmentSlot(
            EquippedDict, CharacterCardSlot, equipmentSourceSlot);
        if (result == MoveEquipmentResult.Ok)
        {
            CharacterCardSlot = newSlot;
            EquipmentChanged?.Invoke();
            CharacterChanged?.Invoke();
        }
        return result;
    }

    public MoveEquipmentResult MoveEquipmentSlotToSlot(EquipmentSlot from, EquipmentSlot to)
    {
        // 阻擋：若目標槽是角色卡所在槽，要走 SwapCharacterWithEquipmentSlot 路徑。
        if (to == CharacterCardSlot) return SwapCharacterWithEquipmentSlot(from);
        // 阻擋：來源槽是角色卡所在槽 → 不能用裝備搬法（角色卡走 MoveCharacterCardToSlot）。
        if (from == CharacterCardSlot) return MoveEquipmentResult.SourceEmpty;
        var result = EquipmentManager.MoveSlotToSlot(EquippedDict, _equipmentCatalog, from, to);
        if (result == MoveEquipmentResult.Ok) EquipmentChanged?.Invoke();
        return result;
    }

    public MoveEquipmentResult MoveEquipmentSlotToBackpack(EquipmentSlot from)
    {
        // 角色卡所在槽不能透過裝備路徑搬到背包（角色卡禁背包）。
        if (from == CharacterCardSlot) return MoveEquipmentResult.IncompatibleSlot;
        var result = EquipmentManager.MoveSlotToBackpack(EquippedDict, BackpackList, from);
        if (result == MoveEquipmentResult.Ok) EquipmentChanged?.Invoke();
        return result;
    }

    public MoveEquipmentResult MoveEquipmentBackpackToSlot(int backpackIndex, EquipmentSlot to)
    {
        // 目標槽是角色卡所在槽 → 角色卡需移到背包，但角色卡禁背包 → 拒絕。
        if (to == CharacterCardSlot) return MoveEquipmentResult.IncompatibleSlot;
        var result = EquipmentManager.MoveBackpackToSlot(
            EquippedDict, BackpackList, _equipmentCatalog, backpackIndex, to);
        if (result == MoveEquipmentResult.Ok) EquipmentChanged?.Invoke();
        return result;
    }

    private void DrawToHandLimit()
    {
        if (_state is null)
        {
            // standalone：DeckService.DrawOne 抽牌堆空時自動重洗棄牌堆（規格書 §3.4.2）
            while (_hand.Count < HandSizeMax)
            {
                var card = _actionDeck.DrawOne();
                if (card is null) break;
                _hand.Add(card);
            }
            HandChanged?.Invoke(_hand);
            HandSizeChanged?.Invoke(_hand.Count, HandSizeMax);
        }
        else
        {
            // state-mode：規格書 §3.1.3 / §3.4.2 — 抽牌堆空時把棄牌堆洗回；兩堆都空才停
            var sHand = _state.CurrentPlayer.Hand;
            var sDeck = _state.CurrentPlayer.Deck;
            var sDiscard = _state.CurrentPlayer.Discard;
            while (sHand.Count < HandSizeMax)
            {
                if (sDeck.Count == 0)
                {
                    if (sDiscard.Count == 0) break;
                    ReshuffleDiscardIntoDeck();
                }
                var top = sDeck[0];
                sDeck.RemoveAt(0);
                sHand.Add(top);
            }
            HandChanged?.Invoke(BuildHandFromState());
            HandSizeChanged?.Invoke(sHand.Count, HandSizeMax);
        }
    }

    /// <summary>
    /// state-mode 棄牌堆重洗回抽牌堆（規格書 §3.1.3）。
    /// Fisher-Yates；seed 由 RngSeed × 大回合 × Discard.Count 衍生（deterministic per-turn）。
    /// </summary>
    private void ReshuffleDiscardIntoDeck()
    {
        if (_state is null) return;
        var sDeck = _state.CurrentPlayer.Deck;
        var sDiscard = _state.CurrentPlayer.Discard;
        if (sDiscard.Count == 0) return;

        var seed = unchecked(_state.RngSeed * 31 + _state.CurrentBigRound * 7 + sDiscard.Count);
        var rng = new Random(seed);
        var pool = new List<string>(sDiscard);
        sDiscard.Clear();
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        sDeck.AddRange(pool);
    }

    /// <summary>
    /// 統一 AP 消耗（規格書 §3.1.4 + §3.7 同伴代消耗）。
    /// 流程：逐 AP 試同伴代消耗（隨機 50%）→ 不命中算到主角頭上。
    /// 若最後主角 AP 不夠付，rollback 同伴已扣的 AP，回 false。
    /// </summary>
    public bool TryConsumeAp(int cost)
    {
        if (cost <= 0) return true;

        var substituted = new List<CompanionAiState>();
        int heroNeeded = 0;
        for (int i = 0; i < cost; i++)
        {
            var sub = _companionAp.TrySubstitute(_companions);
            if (sub != null) substituted.Add(sub);
            else heroNeeded++;
        }

        if (Ap < heroNeeded)
        {
            // rollback 同伴扣的 AP（CompanionApService 已實扣）
            foreach (var c in substituted) c.RemainingAp++;
            return false;
        }

        Ap -= heroNeeded;
        ApChanged?.Invoke(Ap, ApMax);
        if (substituted.Count > 0)
        {
            foreach (var c in substituted) CompanionSubstituted?.Invoke(c);
            CompanionApChangedEvent?.Invoke();
        }
        return true;
    }

    /// <summary>
    /// Phase 2 任務 11 Stage 2c：state-mode 從 state.TileMap 取 PlacedTile，
    /// 透過 <see cref="TileVisualProfileResolver"/> 翻譯為 TileData；
    /// standalone 從內部 _tiles 取（Phase 1+2 既有路徑）。
    /// </summary>
    public TileData GetTile(int row, int col)
    {
        if (_state is null) return _tiles[row, col];
        return ResolveStateTile(row, col);
    }

    /// <summary>
    /// state-mode：取得 (row, col) 上的 tile id（如 "village-store"），未放置回 null；
    /// standalone：永遠回 null（無 tile id 概念，僅 MapTerrain enum）。
    /// 用於 MainMapRenderer 在主地圖卡面渲染中文卡名。
    /// </summary>
    public string? GetTileId(int row, int col)
    {
        if (_state is null) return null;
        return _state.TileMap.TryGetValue((col, row), out var placed) ? placed.TileId : null;
    }

    private TileData ResolveStateTile(int row, int col)
    {
        // state.TileMap key 是 (X=col, Y=row)
        if (_state!.TileMap.TryGetValue((col, row), out var placed) && _module is not null
            && _module.Tiles.TryGetValue(placed.TileId, out var tileDef))
        {
            var terrain = TileVisualProfileResolver.ResolveTerrain(tileDef);
            // IsExplored = ExplorationLevel >= Unfamiliar
            // 語意：Unknown(-2) = 剛 placed 尚未踏入 → 顯示 card-back；
            // Unfamiliar(-1)+ = 已踏入過、看過真實地形 → 顯示 PNG。
            // 起始格 CreateNew 預設 Unfamiliar → 自動 IsExplored=true（與 Phase 1+2 行為一致）。
            // PromoteOnFirstEnter 升 Unknown→Unfamiliar 後該格也轉為 explored。
            var explored = (int)placed.Level >= (int)ExplorationLevel.Unfamiliar;
            return new TileData(row, col, terrain, IsPlaced: true, IsExplored: explored);
        }
        return new TileData(row, col, MapTerrain.Forest, IsPlaced: false, IsExplored: false);
    }

    public bool IsLegalPlacement(int row, int col)
    {
        if (!IsInBounds(row, col)) return false;
        if (GetTile(row, col).IsPlaced) return false;
        return HasPlacedNeighbor(row, col);
    }

    public bool IsLegalMoveTarget(int row, int col)
    {
        if (!IsInBounds(row, col)) return false;
        if (!GetTile(row, col).IsPlaced) return false;
        var (pr, pc) = PlayerPos;
        return Math.Abs(row - pr) + Math.Abs(col - pc) == 1;
    }

    /// <summary>
    /// v1.12 Stage 2 — state-mode：填批次（最多 3 張）；standalone 行為不變（單張 dequeue）。
    /// state-mode 不再直接設定 HeldTileId — 由 UI 呼 <see cref="SelectFromBatch"/> 從批次選 1 張。
    /// 既有批次（如先前 Cancel 留下的）會被沿用，不重新抽。
    /// </summary>
    public bool BeginMapExpand()
    {
        if (Mode != InteractionMode.Idle) return false;
        if (_state is null)
        {
            if (_tileDeck.Count == 0) return false;
            _heldTile = _tileDeck.Dequeue();
        }
        else
        {
            var batch = _state.TileChoiceBatch;
            if (batch.Count == 0)
            {
                int fill = Math.Min(3, _state.TileDeck.Count);
                for (int i = 0; i < fill; i++)
                {
                    batch.Add(_state.TileDeck[0]);
                    _state.TileDeck.RemoveAt(0);
                }
            }
            if (batch.Count == 0) return false; // 牌堆與批次皆空
        }
        Mode = InteractionMode.MapExpand;
        ModeChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// v1.12 Stage 2 — state-mode：從批次選一張為 HeldTileId（規格書 §3.1.4）。
    /// 若已持有：與 batch[idx] 互換（re-select），idx 位置不變、批次長度不變；
    /// 若未持有：取 batch[idx] 為 held，並 RemoveAt(idx)（緊湊 List）。
    /// standalone 不支援批次，永遠回 false。
    /// </summary>
    public bool SelectFromBatch(int idx)
    {
        if (_state is null) return false;
        if (Mode != InteractionMode.MapExpand) return false;
        var batch = _state.TileChoiceBatch;
        if (idx < 0 || idx >= batch.Count) return false;

        var heldId = _state.CurrentPlayer.HeldTileId;
        if (heldId is null)
        {
            _state.CurrentPlayer.HeldTileId = batch[idx];
            batch.RemoveAt(idx);
        }
        else
        {
            // re-select：互換（idx 位置保留，方便 UI slot 視覺穩定）
            _state.CurrentPlayer.HeldTileId = batch[idx];
            batch[idx] = heldId;
        }
        ModeChanged?.Invoke();
        return true;
    }

    public bool TryPlaceHeldTile(int row, int col)
    {
        if (Mode != InteractionMode.MapExpand) return false;
        if (HeldTile is null) return false;
        if (!IsLegalPlacement(row, col)) return false;

        var terrain = HeldTile.Value;
        if (_state is null)
        {
            _tiles[row, col] = _tiles[row, col] with { Terrain = terrain, IsPlaced = true };
            _heldTile = null;
        }
        else
        {
            // state.TileMap key = (X=col, Y=row)；新 PlacedTile 預設 Level=Unknown 觸發後續 onEnter 探索
            var heldId = _state.CurrentPlayer.HeldTileId!;
            _state.TileMap[(col, row)] = new PlacedTile
            {
                TileId = heldId,
                Level = ExplorationLevel.Unknown,
            };
            _state.CurrentPlayer.HeldTileId = null;
        }
        Mode = InteractionMode.Idle;
        TilePlaced?.Invoke(terrain, row, col);
        TileChanged?.Invoke(row, col);
        ModeChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// v1.12 Stage 2 — state-mode：若持有則退回批次末尾（緊湊 List）；批次本身保留供下次 Begin 沿用；
    /// standalone 行為不變（held 退回 _tileDeck 最前端）。Mode → Idle。
    /// </summary>
    public void CancelMapExpand()
    {
        if (Mode != InteractionMode.MapExpand) return;
        if (_state is null)
        {
            if (_heldTile is null) return;
            var newDeck = new Queue<MapTerrain>();
            newDeck.Enqueue(_heldTile.Value);
            foreach (var t in _tileDeck) newDeck.Enqueue(t);
            _tileDeck.Clear();
            foreach (var t in newDeck) _tileDeck.Enqueue(t);
            _heldTile = null;
        }
        else
        {
            // state-mode：held（若有）退回 batch 末尾，批次保留待下次 BeginMapExpand 沿用
            var heldId = _state.CurrentPlayer.HeldTileId;
            if (heldId is not null)
            {
                _state.TileChoiceBatch.Add(heldId);
                _state.CurrentPlayer.HeldTileId = null;
            }
        }
        Mode = InteractionMode.Idle;
        ModeChanged?.Invoke();
    }

    public void BeginMoveMode()
    {
        if (Mode != InteractionMode.Idle) return;
        Mode = InteractionMode.Move;
        ModeChanged?.Invoke();
    }

    public void CancelMoveMode()
    {
        if (Mode != InteractionMode.Move) return;
        Mode = InteractionMode.Idle;
        ModeChanged?.Invoke();
    }

    public MovePlayerResult TryMovePlayerTo(int newRow, int newCol)
    {
        if (!IsLegalMoveTarget(newRow, newCol)) return MovePlayerResult.IllegalTarget;

        int apCost = FirstMoveUsedThisTurn ? 1 : 0;
        if (apCost > 0 && !TryConsumeAp(apCost)) return MovePlayerResult.NotEnoughAp;

        var (oldRow, oldCol) = PlayerPos;
        PlayerPos = (newRow, newCol);
        FirstMoveUsedThisTurn = true;

        // 標記目標格為已探索
        if (_state is null)
        {
            if (!_tiles[newRow, newCol].IsExplored)
            {
                _tiles[newRow, newCol] = _tiles[newRow, newCol] with { IsExplored = true };
                TileChanged?.Invoke(newRow, newCol);
            }
        }
        else
        {
            // state-mode：把目標格的 Level 升到 Familiar（IsExplored 派生條件）
            if (_state.TileMap.TryGetValue((newCol, newRow), out var placed)
                && (int)placed.Level < (int)ExplorationLevel.Familiar)
            {
                placed.Level = ExplorationLevel.Familiar;
                TileChanged?.Invoke(newRow, newCol);
            }
        }

        if (Mode == InteractionMode.Move)
        {
            Mode = InteractionMode.Idle;
            ModeChanged?.Invoke();
        }

        PlayerMoved?.Invoke(oldRow, oldCol, newRow, newCol);
        return MovePlayerResult.Ok;
    }

    public ObserveResult Observe(IRollProvider roll, int skillBonus = 3)
    {
        int apCost = FirstObserveUsedThisTurn ? 2 : 0;
        if (apCost > 0 && !TryConsumeAp(apCost))
            return new ObserveResult(false, false, 0, 0, 0, 0, false, false);

        var (d1, d2) = roll.Roll2d6();
        var total = d1 + d2 + skillBonus;
        var success = total >= ObserveTn;
        var isD6 = d1 == 6 && d2 == 6;
        var isD1 = d1 == 1 && d2 == 1;

        FirstObserveUsedThisTurn = true;
        ObserveResolved?.Invoke(d1 + d2, skillBonus, ObserveTn, success, isD6, isD1);
        return new ObserveResult(true, success, d1, d2, skillBonus, ObserveTn, isD6, isD1);
    }

    /// <summary>
    /// 嘗試出牌（規格書 §3.4.1 / §3.1.4）。
    /// 1. 檢查手牌中有此 cardId
    /// 2. 檢查 AP 充足（含同伴代消耗）
    /// 3. 從 hand 移除、進 discard pile、扣 AP
    /// 4. 觸發事件（OnPlay 效果不在本 Stage 範圍）
    /// </summary>
    public PlayCardResult TryPlayCard(string cardId)
    {
        // Hand getter 已 dispatch（state-mode 投影自 state.CurrentPlayer.Hand × module.ActionCards）
        var card = Hand.FirstOrDefault(c => c.Id == cardId);
        if (card is null) return new PlayCardResult(false, "找不到該卡（不在手牌）", 0);

        if (card.Cost > 0 && !TryConsumeAp(card.Cost))
            return new PlayCardResult(false, $"AP 不足（需 {card.Cost} AP）", card.Cost);

        if (_state is null)
        {
            _hand.Remove(card);
            _actionDeck.DiscardCard(card);
            HandChanged?.Invoke(_hand);
            HandSizeChanged?.Invoke(_hand.Count, HandSizeMax);
        }
        else
        {
            _state.CurrentPlayer.Hand.Remove(cardId);
            _state.CurrentPlayer.Discard.Add(cardId);
            HandChanged?.Invoke(BuildHandFromState());
            HandSizeChanged?.Invoke(_state.CurrentPlayer.Hand.Count, HandSizeMax);
        }
        return new PlayCardResult(true, $"打出「{card.Name}」", card.Cost);
    }

    public RestResult Rest()
    {
        if (Ap <= 0 || Hp >= HpMax) return new RestResult(0, 0);
        var apSpent = Ap;
        var hpGain = Math.Min(apSpent, HpMax - Hp);
        Ap = 0;
        Hp += hpGain;
        ApChanged?.Invoke(Ap, ApMax);
        HpChanged?.Invoke(Hp);
        return new RestResult(apSpent, hpGain);
    }

    public bool AdvanceTurn()
    {
        if (Mode != InteractionMode.Idle) return false;
        if (Turn >= TurnLimit) return false;

        Turn++;
        Ap = ApMax;
        FirstMoveUsedThisTurn = false;
        FirstObserveUsedThisTurn = false;

        // Draw 階段：補手牌至上限
        DrawToHandLimit();

        // 同伴 AP 重置
        foreach (var c in _companions) c.ResetForNewTurn();

        TurnChanged?.Invoke(Turn);
        ApChanged?.Invoke(Ap, ApMax);
        CompanionApChangedEvent?.Invoke();
        return true;
    }

    public void SetCameraOffset(float rowOffset, float colOffset)
    {
        CameraOffset = (rowOffset, colOffset);
        CameraOffsetChanged?.Invoke();
    }

    public void ResetCameraToPlayer()
    {
        CameraOffset = (0f, 0f);
        CameraOffsetChanged?.Invoke();
    }

    public static bool IsInBounds(int row, int col)
        => row >= 0 && row < Size && col >= 0 && col < Size;

    /// <summary>
    /// v1.12 §1.5 / §3.1.4 — Tag 配對相容性判定（OR 邏輯）。
    /// 任一邊 tag list 為空 → 視為「中立」自動相容（向下相容無 tag 的舊模組）；
    /// 否則需至少共享 1 個 tag。
    /// 用於：放置時候選 vs 相鄰已放置；移動時當前格 vs 目標格。
    /// </summary>
    public static bool TagsCompatible(
        IReadOnlyList<string>? candidate,
        IReadOnlyList<string>? neighbor)
    {
        if (candidate is null || candidate.Count == 0) return true;
        if (neighbor is null || neighbor.Count == 0) return true;
        for (int i = 0; i < candidate.Count; i++)
        {
            for (int j = 0; j < neighbor.Count; j++)
            {
                if (candidate[i] == neighbor[j]) return true;
            }
        }
        return false;
    }

    private bool HasPlacedNeighbor(int row, int col)
    {
        return (IsInBounds(row - 1, col) && GetTile(row - 1, col).IsPlaced)
            || (IsInBounds(row + 1, col) && GetTile(row + 1, col).IsPlaced)
            || (IsInBounds(row, col - 1) && GetTile(row, col - 1).IsPlaced)
            || (IsInBounds(row, col + 1) && GetTile(row, col + 1).IsPlaced);
    }
}

public readonly record struct TileData(
    int Row, int Col, MapTerrain Terrain, bool IsPlaced, bool IsExplored);

public enum MovePlayerResult { Ok, IllegalTarget, NotEnoughAp }

public readonly record struct ObserveResult(
    bool Performed, bool Success, int D1, int D2, int SkillBonus, int Tn, bool IsDouble6, bool IsDouble1);

public readonly record struct RestResult(int ApSpent, int HpGained);

public readonly record struct PlayCardResult(bool Success, string Message, int ApSpent);

public interface IRollProvider
{
    (int D1, int D2) Roll2d6();
}
