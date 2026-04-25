namespace CardNarrative.Core.Map;

/// <summary>
/// Phase 1 Task 2/4/5/6 — 9×9 主地圖資料模型（核心邏輯層，無 Godot 依賴）。
/// 對應規格書 §1.5 / §5.1.5 / §3.1.2 / §3.1.3 / §3.1.4。
///
/// Task 6 新增：
/// - Turn / 回合計數
/// - Ap / 行動點（規格書 §3.1.1 上限 3）
/// - HandSize / 手牌計數（規格書 §3.1.3 上限 5；目前為 placeholder 計數，無實際卡牌）
/// - AdvanceTurn() — NEXT TURN 推進到下一回合（簡化版：只走 TurnEnd → Draw，跳過 EventCheck）
/// - 移動 / 觀察 / 休息 的 AP 規則（§3.1.4）
/// </summary>
public sealed class WorldMap
{
    public const int Size = 9;
    public const int InitialPlayerRow = 4;
    public const int InitialPlayerCol = 4;

    // === 規格書 §3.1 數值上限 ===
    public const int ApMax = 3;          // 主角 AP 上限（規格書 §3.1）
    public const int HandSizeMax = 5;    // 主角手牌上限（規格書 §3.1）
    public const int TurnLimit = 30;     // 失敗條件：> 30 回合（規格書 §1.2）
    public const int ObserveTn = 10;     // demo 觀察判定 TN

    private readonly TileData[,] _tiles = new TileData[Size, Size];
    private readonly Queue<MapTerrain> _tileDeck;

    public (int Row, int Col) PlayerPos { get; private set; } = (InitialPlayerRow, InitialPlayerCol);
    public (float Row, float Col) CameraOffset { get; private set; } = (0f, 0f);

    public InteractionMode Mode { get; private set; } = InteractionMode.Idle;
    public MapTerrain? HeldTile { get; private set; }

    // === Hp ===
    public int Hp { get; private set; } = 12;
    public int HpMax { get; init; } = 12;

    // === Task 6 新增：回合與 AP ===
    /// <summary>當前回合數（從 1 開始）。</summary>
    public int Turn { get; private set; } = 1;

    /// <summary>當前 AP（每回合 Draw 階段重置至 ApMax）。</summary>
    public int Ap { get; private set; } = ApMax;

    /// <summary>當前手牌數（demo 計數，每回合 Draw 補至 HandSizeMax）。</summary>
    public int HandSize { get; private set; } = HandSizeMax;

    /// <summary>本回合是否已用過第 1 次「免費移動」（§3.1.4）。</summary>
    public bool FirstMoveUsedThisTurn { get; private set; }

    /// <summary>本回合是否已用過第 1 次「免費觀察」（§3.1.4）。</summary>
    public bool FirstObserveUsedThisTurn { get; private set; }

    public IReadOnlyList<MapTerrain> NextTilePreview => _tileDeck.Take(2).ToArray();
    public int RemainingTiles => _tileDeck.Count;

    public event Action<int, int>? TileChanged;
    public event Action<int, int, int, int>? PlayerMoved;
    public event Action? CameraOffsetChanged;
    public event Action? ModeChanged;
    public event Action<MapTerrain, int, int>? TilePlaced;
    public event Action<int>? HpChanged;
    /// <summary>回合變更（含 Draw 階段重置 AP / 補手牌完畢後）。Payload = newTurn。</summary>
    public event Action<int>? TurnChanged;
    /// <summary>AP 變更。Payload = (newAp, apMax)。</summary>
    public event Action<int, int>? ApChanged;
    /// <summary>手牌數變更。Payload = (newHandSize, handSizeMax)。</summary>
    public event Action<int, int>? HandSizeChanged;
    /// <summary>觀察判定完成。Payload = (rolledTotal, statBonus, tn, success, isDouble6, isDouble1)。</summary>
    public event Action<int, int, int, bool, bool, bool>? ObserveResolved;

    public WorldMap()
    {
        for (int r = 0; r < Size; r++)
        for (int c = 0; c < Size; c++)
        {
            _tiles[r, c] = new TileData(r, c, MapTerrain.Forest, IsPlaced: false, IsExplored: false);
        }

        _tiles[InitialPlayerRow, InitialPlayerCol] = new TileData(
            InitialPlayerRow, InitialPlayerCol, MapTerrain.Building,
            IsPlaced: true, IsExplored: true);

        _tileDeck = new Queue<MapTerrain>(new[]
        {
            MapTerrain.Path,
            MapTerrain.Forest,
            MapTerrain.Grass,
            MapTerrain.Water,
            MapTerrain.Path,
            MapTerrain.Mountain,
            MapTerrain.Forest,
            MapTerrain.Grass,
            MapTerrain.Building,
            MapTerrain.Path,
        });
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

    /// <summary>
    /// 嘗試移動。AP 規則（§3.1.4）：
    /// - 本回合第 1 次移動 = 免費（FirstMoveUsedThisTurn 標記）
    /// - 第 2 次起每次 1 AP
    /// 若 AP 不足返回 false 不移動。
    /// </summary>
    public MovePlayerResult TryMovePlayerTo(int newRow, int newCol)
    {
        if (!IsLegalMoveTarget(newRow, newCol)) return MovePlayerResult.IllegalTarget;

        // 計算 AP 消耗
        int apCost = FirstMoveUsedThisTurn ? 1 : 0;
        if (Ap < apCost) return MovePlayerResult.NotEnoughAp;

        var (oldRow, oldCol) = PlayerPos;
        PlayerPos = (newRow, newCol);

        if (apCost > 0)
        {
            Ap -= apCost;
            ApChanged?.Invoke(Ap, ApMax);
        }
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

    /// <summary>
    /// 觀察判定（規格書 §3.1.4 + §3.3）：
    /// - 本回合第 1 次觀察 = 免費
    /// - 第 2 次起每次 2 AP
    /// - 公式：2d6 + Skill (demo=3) vs TN(10)
    /// </summary>
    public ObserveResult Observe(IRollProvider roll, int skillBonus = 3)
    {
        int apCost = FirstObserveUsedThisTurn ? 2 : 0;
        if (Ap < apCost) return new ObserveResult(false, false, 0, 0, 0, 0, false, false);

        var (d1, d2) = roll.Roll2d6();
        var total = d1 + d2 + skillBonus;
        var success = total >= ObserveTn;
        var isD6 = d1 == 6 && d2 == 6;
        var isD1 = d1 == 1 && d2 == 1;

        if (apCost > 0)
        {
            Ap -= apCost;
            ApChanged?.Invoke(Ap, ApMax);
        }
        FirstObserveUsedThisTurn = true;

        ObserveResolved?.Invoke(d1 + d2, skillBonus, ObserveTn, success, isD6, isD1);
        return new ObserveResult(true, success, d1, d2, skillBonus, ObserveTn, isD6, isD1);
    }

    /// <summary>
    /// 休息：消耗剩餘全部 AP，每 1 AP 回 1 HP（規格書 §3.1.4）。
    /// </summary>
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

    /// <summary>
    /// 推進到下一回合。簡化流程（Task 6）：
    /// TurnEnd → Turn++ → Draw（重置 AP + 補手牌至上限）→ 重置「首次免費」旗標
    /// 若 Turn > TurnLimit 不推進，回傳 false。
    /// </summary>
    public bool AdvanceTurn()
    {
        if (Mode != InteractionMode.Idle) return false;
        if (Turn >= TurnLimit) return false; // 達上限不再推進（後續 Phase 接結局結算）

        Turn++;
        // Draw 階段
        Ap = ApMax;
        HandSize = HandSizeMax;
        FirstMoveUsedThisTurn = false;
        FirstObserveUsedThisTurn = false;

        TurnChanged?.Invoke(Turn);
        ApChanged?.Invoke(Ap, ApMax);
        HandSizeChanged?.Invoke(HandSize, HandSizeMax);
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
    int Row,
    int Col,
    MapTerrain Terrain,
    bool IsPlaced,
    bool IsExplored);

/// <summary>移動結果（Task 6 含 AP 不足分支）。</summary>
public enum MovePlayerResult { Ok, IllegalTarget, NotEnoughAp }

/// <summary>觀察判定結果（demo 用）。</summary>
public readonly record struct ObserveResult(
    bool Performed, bool Success, int D1, int D2, int SkillBonus, int Tn, bool IsDouble6, bool IsDouble1);

/// <summary>休息結果。</summary>
public readonly record struct RestResult(int ApSpent, int HpGained);

/// <summary>抽象 2d6 來源，方便注入 fake dice 測試。</summary>
public interface IRollProvider
{
    (int D1, int D2) Roll2d6();
}
