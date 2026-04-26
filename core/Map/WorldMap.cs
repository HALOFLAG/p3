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
    private readonly Dictionary<EquipmentSlot, string?> _equipped = new();
    private readonly List<string> _backpack = new();
    private readonly Dictionary<string, Equipment> _equipmentCatalog = new();

    // === Character / Companion 卡片（驅動 LeftPanel 主角區 + 夥伴區） ===
    private Character? _character;
    private NpcCompanion? _companion;
    private int _companionHp;

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
    /// state-mode 從 _heldTileId 派生 MapTerrain（透過 TileVisualProfileResolver）；
    /// standalone 使用 _heldTile 內部欄位。
    /// </summary>
    public MapTerrain? HeldTile
    {
        get
        {
            if (_state is null) return _heldTile;
            if (_heldTileId is null || _module is null) return null;
            if (!_module.Tiles.TryGetValue(_heldTileId, out var tile)) return null;
            return TileVisualProfileResolver.ResolveTerrain(tile);
        }
        private set
        {
            if (_state is null) _heldTile = value;
            // state-mode：HeldTile 由 _heldTileId 決定，外部設值無意義
            // 內部呼叫 BeginMapExpand / TryPlaceHeldTile / CancelMapExpand 時直接操作 _heldTileId
        }
    }
    /// <summary>state-mode 持有的 tile id（從 state.TileDeck 抽出）。standalone 不使用。</summary>
    private string? _heldTileId;

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

    /// <summary>角色卡目前佔住的裝備槽位（規格書 §3.4.3 中央格 → §3-7）。預設 Head。</summary>
    public EquipmentSlot CharacterCardSlot { get; private set; } = EquipmentSlot.Head;
    public Character? Character => _character;
    public NpcCompanion? Companion => _companion;
    public int CompanionHp => _companionHp;
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

    /// <summary>
    /// 本回合是否已用過首次觀察。state 無等價欄位 → 兩模式皆用 WorldMap 內部欄位。
    /// 完整 EffectsService（Task 13）後若有對應 state 欄位再 dispatch。
    /// </summary>
    public bool FirstObserveUsedThisTurn { get; private set; }

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

    /// <summary>當前裝備配置（slot → equipmentId）。Read-only 對外。</summary>
    public IReadOnlyDictionary<EquipmentSlot, string?> Equipped => _equipped;
    /// <summary>背包內容（read-only）。</summary>
    public IReadOnlyList<string> Backpack => _backpack;
    /// <summary>裝備目錄（id → Equipment）。</summary>
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

        // 裝備 8 個主槽預設為 null（空）
        foreach (EquipmentSlot s in new[] {
            EquipmentSlot.Head, EquipmentSlot.Weapon, EquipmentSlot.OffHand,
            EquipmentSlot.Body, EquipmentSlot.AccessoryA, EquipmentSlot.AccessoryB,
            EquipmentSlot.Feet, EquipmentSlot.Utility,
        })
        {
            _equipped[s] = null;
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
        _backpack.Clear();
        foreach (var id in initialBackpack)
        {
            if (_backpack.Count >= EquipmentManager.BackpackMax) break;
            if (!_equipmentCatalog.ContainsKey(id)) continue;
            _backpack.Add(id);
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

    /// <summary>載入夥伴卡（單一夥伴；多夥伴未來擴充）。</summary>
    public void LoadCompanion(NpcCompanion? companion)
    {
        _companion = companion;
        _companionHp = companion?.Hp ?? 0;
        CompanionChanged?.Invoke();
    }

    /// <summary>角色卡 → 主槽。背包永遠拒絕（由 UI 端阻擋；此處不接背包輸入）。</summary>
    public MoveEquipmentResult MoveCharacterCardToSlot(EquipmentSlot target)
    {
        var (result, newSlot) = EquipmentManager.MoveCharacterCardSlotToSlot(
            _equipped, _equipmentCatalog, CharacterCardSlot, target);
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
            _equipped, CharacterCardSlot, equipmentSourceSlot);
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
        var result = EquipmentManager.MoveSlotToSlot(_equipped, _equipmentCatalog, from, to);
        if (result == MoveEquipmentResult.Ok) EquipmentChanged?.Invoke();
        return result;
    }

    public MoveEquipmentResult MoveEquipmentSlotToBackpack(EquipmentSlot from)
    {
        // 角色卡所在槽不能透過裝備路徑搬到背包（角色卡禁背包）。
        if (from == CharacterCardSlot) return MoveEquipmentResult.IncompatibleSlot;
        var result = EquipmentManager.MoveSlotToBackpack(_equipped, _backpack, from);
        if (result == MoveEquipmentResult.Ok) EquipmentChanged?.Invoke();
        return result;
    }

    public MoveEquipmentResult MoveEquipmentBackpackToSlot(int backpackIndex, EquipmentSlot to)
    {
        // 目標槽是角色卡所在槽 → 角色卡需移到背包，但角色卡禁背包 → 拒絕。
        if (to == CharacterCardSlot) return MoveEquipmentResult.IncompatibleSlot;
        var result = EquipmentManager.MoveBackpackToSlot(
            _equipped, _backpack, _equipmentCatalog, backpackIndex, to);
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
            if (_state.TileDeck.Count == 0) return false;
            _heldTileId = _state.TileDeck[0];
            _state.TileDeck.RemoveAt(0);
        }
        Mode = InteractionMode.MapExpand;
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
            _state!.TileMap[(col, row)] = new PlacedTile
            {
                TileId = _heldTileId!,
                Level = ExplorationLevel.Unknown,
            };
            _heldTileId = null;
        }
        Mode = InteractionMode.Idle;
        TilePlaced?.Invoke(terrain, row, col);
        TileChanged?.Invoke(row, col);
        ModeChanged?.Invoke();
        return true;
    }

    public void CancelMapExpand()
    {
        if (Mode != InteractionMode.MapExpand) return;
        if (HeldTile is null) return;
        if (_state is null)
        {
            var newDeck = new Queue<MapTerrain>();
            newDeck.Enqueue(_heldTile!.Value);
            foreach (var t in _tileDeck) newDeck.Enqueue(t);
            _tileDeck.Clear();
            foreach (var t in newDeck) _tileDeck.Enqueue(t);
            _heldTile = null;
        }
        else
        {
            // 把 heldTileId 放回 state.TileDeck 最前端
            _state!.TileDeck.Insert(0, _heldTileId!);
            _heldTileId = null;
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
