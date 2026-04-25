namespace CardNarrative.Core.Map;

/// <summary>
/// Phase 1 Task 2 + 4 — 9×9 主地圖資料模型（核心邏輯層，無 Godot 依賴）。
/// 對應規格書 §1.5 / §5.1.5 / §3.1.2 / §3.1.4。
///
/// 此版概念：
/// - 9×9 中只有「已放置」(IsPlaced) 的格上才有地塊卡
/// - MapExpand 階段從 TileDeck 抽出一張，放到合法的 4 方向相鄰未放格
/// - 玩家只能在「已放置」格之間移動
/// - 探索狀態（IsExplored）= 玩家曾經走到該格
/// </summary>
public sealed class WorldMap
{
    public const int Size = 9;
    public const int InitialPlayerRow = 4;
    public const int InitialPlayerCol = 4;

    private readonly TileData[,] _tiles = new TileData[Size, Size];
    private readonly Queue<MapTerrain> _tileDeck;

    public (int Row, int Col) PlayerPos { get; private set; } = (InitialPlayerRow, InitialPlayerCol);
    public (float Row, float Col) CameraOffset { get; private set; } = (0f, 0f);

    /// <summary>當前互動模式。</summary>
    public InteractionMode Mode { get; private set; } = InteractionMode.Idle;

    /// <summary>MapExpand 模式下持有的地塊卡（規格書 §3.1.2 步驟 3）。</summary>
    public MapTerrain? HeldTile { get; private set; }

    /// <summary>玩家 HP — Task 5 「休息」用。Phase 2 整合 Character 後改用。</summary>
    public int Hp { get; private set; } = 12;
    public int HpMax { get; init; } = 12;

    /// <summary>NEXT 預覽：接下來要抽的兩張（規格書 §3.1.2、§4.1 #14）。</summary>
    public IReadOnlyList<MapTerrain> NextTilePreview => _tileDeck.Take(2).ToArray();

    public int RemainingTiles => _tileDeck.Count;

    public event Action<int, int>? TileChanged;
    public event Action<int, int, int, int>? PlayerMoved;
    public event Action? CameraOffsetChanged;
    public event Action? ModeChanged;
    public event Action<MapTerrain, int, int>? TilePlaced;
    public event Action<int>? HpChanged;

    public WorldMap()
    {
        // 9×9 初始為「未放置 + 未探索」狀態
        for (int r = 0; r < Size; r++)
        for (int c = 0; c < Size; c++)
        {
            _tiles[r, c] = new TileData(r, c, MapTerrain.Forest, IsPlaced: false, IsExplored: false);
        }

        // 初始地塊（4,4），規格書 §3.1.0：固定中央
        _tiles[InitialPlayerRow, InitialPlayerCol] = new TileData(
            InitialPlayerRow, InitialPlayerCol, MapTerrain.Building,
            IsPlaced: true, IsExplored: true);

        // Task 4 demo deck：硬編碼 10 張地塊代表性混合
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

    /// <summary>檢查 (row,col) 是否為合法放置區（MapExpand 用，規格書 §3.1.2）。</summary>
    public bool IsLegalPlacement(int row, int col)
    {
        if (!IsInBounds(row, col)) return false;
        if (_tiles[row, col].IsPlaced) return false;
        // 4 方向至少一個相鄰格已放置
        return HasPlacedNeighbor(row, col);
    }

    /// <summary>檢查 (row,col) 是否為玩家可移動目標（4 方向相鄰且已放置）。</summary>
    public bool IsLegalMoveTarget(int row, int col)
    {
        if (!IsInBounds(row, col)) return false;
        if (!_tiles[row, col].IsPlaced) return false;
        var (pr, pc) = PlayerPos;
        return Math.Abs(row - pr) + Math.Abs(col - pc) == 1;
    }

    /// <summary>規格書 §3.1.2 步驟 1：從 deck 抽下一張進入 MapExpand 模式。</summary>
    public bool BeginMapExpand()
    {
        if (Mode != InteractionMode.Idle) return false;
        if (_tileDeck.Count == 0) return false;
        HeldTile = _tileDeck.Dequeue();
        Mode = InteractionMode.MapExpand;
        ModeChanged?.Invoke();
        return true;
    }

    /// <summary>規格書 §3.1.2 步驟 4：把持有地塊放到合法格。</summary>
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

    /// <summary>取消 MapExpand（地塊放回 deck 最前）。</summary>
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

    /// <summary>進入移動模式（規格書 §3.1.4，玩家從觸發器選「移動」）。</summary>
    public void BeginMoveMode()
    {
        if (Mode != InteractionMode.Idle) return;
        Mode = InteractionMode.Move;
        ModeChanged?.Invoke();
    }

    /// <summary>取消移動模式（規格書 §3.1.4 右鍵取消）。</summary>
    public void CancelMoveMode()
    {
        if (Mode != InteractionMode.Move) return;
        Mode = InteractionMode.Idle;
        ModeChanged?.Invoke();
    }

    /// <summary>嘗試把玩家移到指定格（4 方向相鄰、已放置）。</summary>
    public bool TryMovePlayerTo(int newRow, int newCol)
    {
        if (!IsLegalMoveTarget(newRow, newCol)) return false;

        var (oldRow, oldCol) = PlayerPos;
        PlayerPos = (newRow, newCol);

        if (!_tiles[newRow, newCol].IsExplored)
        {
            _tiles[newRow, newCol] = _tiles[newRow, newCol] with { IsExplored = true };
            TileChanged?.Invoke(newRow, newCol);
        }

        // 移動後若處於 Move 模式，回到 Idle
        if (Mode == InteractionMode.Move)
        {
            Mode = InteractionMode.Idle;
            ModeChanged?.Invoke();
        }

        PlayerMoved?.Invoke(oldRow, oldCol, newRow, newCol);
        return true;
    }

    /// <summary>休息：HP +1（Task 5 簡化版，Task 6 改為消耗剩餘 AP）。</summary>
    public void Rest()
    {
        if (Hp >= HpMax) return;
        Hp = Math.Min(HpMax, Hp + 1);
        HpChanged?.Invoke(Hp);
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

/// <summary>單一地塊純資料（規格書 §5.1.5 簡化版）。</summary>
public readonly record struct TileData(
    int Row,
    int Col,
    MapTerrain Terrain,
    bool IsPlaced,
    bool IsExplored);
