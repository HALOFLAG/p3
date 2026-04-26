using CardNarrative.Core.Cards;
using CardNarrative.Core.Models;

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

    // === Equipment 狀態（規格書 §3.4.3）===
    private readonly Dictionary<EquipmentSlot, string?> _equipped = new();
    private readonly List<string> _backpack = new();
    private readonly Dictionary<string, Equipment> _equipmentCatalog = new();

    // === Character / Companion 卡片（驅動 LeftPanel 主角區 + 夥伴區） ===
    private Character? _character;
    private NpcCompanion? _companion;
    private int _companionHp;

    public (int Row, int Col) PlayerPos { get; private set; } = (InitialPlayerRow, InitialPlayerCol);
    public (float Row, float Col) CameraOffset { get; private set; } = (0f, 0f);

    public InteractionMode Mode { get; private set; } = InteractionMode.Idle;
    public MapTerrain? HeldTile { get; private set; }

    public int Hp { get; private set; } = 12;
    public int HpMax { get; private set; } = 12;

    /// <summary>角色卡目前佔住的裝備槽位（規格書 §3.4.3 中央格 → §3-7）。預設 Head。</summary>
    public EquipmentSlot CharacterCardSlot { get; private set; } = EquipmentSlot.Head;
    public Character? Character => _character;
    public NpcCompanion? Companion => _companion;
    public int CompanionHp => _companionHp;
    public int CompanionHpMax => _companion?.Hp ?? 0;

    public int Turn { get; private set; } = 1;
    public int Ap { get; private set; } = ApMax;
    public int HandSize => _hand.Count;

    public bool FirstMoveUsedThisTurn { get; private set; }
    public bool FirstObserveUsedThisTurn { get; private set; }

    public IReadOnlyList<ActionCard> Hand => _hand;
    public int ActionDeckRemaining => _actionDeck.DrawCount;
    public int ActionDiscardCount => _actionDeck.DiscardCount;
    public IReadOnlyList<CompanionAiState> Companions => _companions;

    /// <summary>當前裝備配置（slot → equipmentId）。Read-only 對外。</summary>
    public IReadOnlyDictionary<EquipmentSlot, string?> Equipped => _equipped;
    /// <summary>背包內容（read-only）。</summary>
    public IReadOnlyList<string> Backpack => _backpack;
    /// <summary>裝備目錄（id → Equipment）。</summary>
    public IReadOnlyDictionary<string, Equipment> EquipmentCatalog => _equipmentCatalog;

    public IReadOnlyList<MapTerrain> NextTilePreview => _tileDeck.Take(3).ToArray();
    public int RemainingTiles => _tileDeck.Count;

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

    public WorldMap() : this(new SystemRandomProvider()) { }

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

    /// <summary>從外部（如 ModuleLoader）注入行動卡 deck，並抽至手牌上限。</summary>
    public void LoadActionDeck(IEnumerable<ActionCard> cards)
    {
        _actionDeck.LoadInitial(cards);
        _hand.Clear();
        DrawToHandLimit();
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
        while (_hand.Count < HandSizeMax)
        {
            var card = _actionDeck.DrawOne();
            if (card is null) break;
            _hand.Add(card);
        }
        HandChanged?.Invoke(_hand);
        HandSizeChanged?.Invoke(_hand.Count, HandSizeMax);
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

    public TileData GetTile(int row, int col) => _tiles[row, col];

    public bool IsLegalPlacement(int row, int col)
    {
        if (!IsInBounds(row, col)) return false;
        if (_tiles[row, col].IsPlaced) return false;
        return HasPlacedNeighbor(row, col);
    }

    public bool IsLegalMoveTarget(int row, int col)
    {
        if (!IsInBounds(row, col)) return false;
        if (!_tiles[row, col].IsPlaced) return false;
        var (pr, pc) = PlayerPos;
        return Math.Abs(row - pr) + Math.Abs(col - pc) == 1;
    }

    public bool BeginMapExpand()
    {
        if (Mode != InteractionMode.Idle) return false;
        if (_tileDeck.Count == 0) return false;
        HeldTile = _tileDeck.Dequeue();
        Mode = InteractionMode.MapExpand;
        ModeChanged?.Invoke();
        return true;
    }

    public bool TryPlaceHeldTile(int row, int col)
    {
        if (Mode != InteractionMode.MapExpand || HeldTile is null) return false;
        if (!IsLegalPlacement(row, col)) return false;

        var terrain = HeldTile.Value;
        _tiles[row, col] = _tiles[row, col] with { Terrain = terrain, IsPlaced = true };
        HeldTile = null;
        Mode = InteractionMode.Idle;
        TilePlaced?.Invoke(terrain, row, col);
        TileChanged?.Invoke(row, col);
        ModeChanged?.Invoke();
        return true;
    }

    public void CancelMapExpand()
    {
        if (Mode != InteractionMode.MapExpand || HeldTile is null) return;
        var newDeck = new Queue<MapTerrain>();
        newDeck.Enqueue(HeldTile.Value);
        foreach (var t in _tileDeck) newDeck.Enqueue(t);
        _tileDeck.Clear();
        foreach (var t in newDeck) _tileDeck.Enqueue(t);
        HeldTile = null;
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

        if (!_tiles[newRow, newCol].IsExplored)
        {
            _tiles[newRow, newCol] = _tiles[newRow, newCol] with { IsExplored = true };
            TileChanged?.Invoke(newRow, newCol);
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
        var card = _hand.FirstOrDefault(c => c.Id == cardId);
        if (card is null) return new PlayCardResult(false, "找不到該卡（不在手牌）", 0);

        if (card.Cost > 0 && !TryConsumeAp(card.Cost))
            return new PlayCardResult(false, $"AP 不足（需 {card.Cost} AP）", card.Cost);

        _hand.Remove(card);
        _actionDeck.DiscardCard(card);
        HandChanged?.Invoke(_hand);
        HandSizeChanged?.Invoke(_hand.Count, HandSizeMax);
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
        return (IsInBounds(row - 1, col) && _tiles[row - 1, col].IsPlaced)
            || (IsInBounds(row + 1, col) && _tiles[row + 1, col].IsPlaced)
            || (IsInBounds(row, col - 1) && _tiles[row, col - 1].IsPlaced)
            || (IsInBounds(row, col + 1) && _tiles[row, col + 1].IsPlaced);
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
