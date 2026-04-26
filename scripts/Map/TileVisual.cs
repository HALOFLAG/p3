using CardNarrative.Core.Map;
using Godot;

namespace HauntedManor.Scripts.Map;

/// <summary>
/// Phase 1 Task 2/4/5 — 單一地塊節點（規格書 §5.1.3 / §5.1.4）。
/// v2：以 Polygon2D + UV mapping 渲染為傾斜向前的梯形（後窄前寬），
/// 取代原本軸對齊 Sprite2D。MainMapRenderer 透過 SetTileQuad(4 corners) 設定形狀。
/// </summary>
public partial class TileVisual : Node2D
{
    [Signal]
    public delegate void TileClickedEventHandler(int row, int col);

    public enum OverlayKind { None, LegalPlacement, MoveTarget, PlayerMark }

    private Polygon2D? _topPolygon;   // 地形美術 + UV
    private Polygon2D? _sidePolygon;  // 前緣下方厚度
    private Polygon2D? _overlayPolygon; // 半透明色蓋層
    private Area2D? _hoverArea;
    private CollisionShape2D? _collShape;

    private MapTerrain _terrain;
    private bool _isPlaced;
    private bool _isExplored;
    private bool _isHovered;
    private OverlayKind _overlayKind = OverlayKind.None;

    // 最後一次 SetTileQuad 的 4 角（local 座標，TileVisual 中心為原點）
    private Vector2 _backLeft, _backRight, _frontRight, _frontLeft;

    public int Row { get; set; }
    public int Col { get; set; }

    private static readonly System.Collections.Generic.Dictionary<MapTerrain, string> TerrainTexturePaths = new()
    {
        { MapTerrain.Forest,   "res://art/tiles/forest.png"   },
        { MapTerrain.Path,     "res://art/tiles/path.png"     },
        { MapTerrain.Grass,    "res://art/tiles/grass.png"    },
        { MapTerrain.Water,    "res://art/tiles/water.png"    },
        { MapTerrain.Mountain, "res://art/tiles/mountain.png" },
        { MapTerrain.Building, "res://art/tiles/building.png" },
    };

    private const string CardBackPath = "res://art/tiles/card-back.png";

    public override void _Ready()
    {
        // 場景檔可能還掛著舊的 Sprite2D / SideFace ColorRect，全部移除
        foreach (var child in GetChildren())
        {
            if (child is Node n) n.QueueFree();
        }

        // 側緣（前緣下方厚度，畫在 top 之下）
        _sidePolygon = new Polygon2D
        {
            Name = "SidePolygon",
            Color = new Color(0.169f, 0.114f, 0.055f, 1f),
            Visible = false,
        };
        AddChild(_sidePolygon);

        // 地形主面
        _topPolygon = new Polygon2D
        {
            Name = "TopPolygon",
            Color = Colors.White,
        };
        AddChild(_topPolygon);

        // Overlay 蓋層
        _overlayPolygon = new Polygon2D
        {
            Name = "OverlayPolygon",
            Visible = false,
        };
        AddChild(_overlayPolygon);

        // Click 命中區
        _hoverArea = new Area2D { Name = "HoverArea", InputPickable = true };
        AddChild(_hoverArea);
        _collShape = new CollisionShape2D { Name = "CollisionShape2D" };
        _hoverArea.AddChild(_collShape);

        _hoverArea.MouseEntered += OnMouseEntered;
        _hoverArea.MouseExited += OnMouseExited;
        _hoverArea.InputEvent += OnAreaInput;

        ApplyTexture();
    }

    public void SetTile(MapTerrain terrain, bool isPlaced, bool isExplored)
    {
        _terrain = terrain;
        _isPlaced = isPlaced;
        _isExplored = isExplored;
        if (_topPolygon != null) ApplyTexture();
        if (_sidePolygon != null) _sidePolygon.Visible = isPlaced;
    }

    public void SetOverlay(OverlayKind kind)
    {
        _overlayKind = kind;
        UpdateOverlay();
    }

    /// <summary>
    /// 設定地塊 4 角（在 TileVisual 的 local 座標系，原點為 TileVisual.Position）。
    /// 順序：後左 → 後右 → 前右 → 前左（順時針）。
    /// </summary>
    public void SetTileQuad(Vector2 backLeft, Vector2 backRight, Vector2 frontRight, Vector2 frontLeft)
    {
        _backLeft = backLeft;
        _backRight = backRight;
        _frontRight = frontRight;
        _frontLeft = frontLeft;

        var quad = new[] { backLeft, backRight, frontRight, frontLeft };

        if (_topPolygon != null)
        {
            _topPolygon.Polygon = quad;
            UpdateTopPolygonUv();
        }

        if (_overlayPolygon != null)
        {
            _overlayPolygon.Polygon = quad;
        }

        if (_sidePolygon != null)
        {
            // 厚度：把前緣往下推約「前後高度差的 12%」做為側面
            var heightSpan = ((frontLeft.Y + frontRight.Y) - (backLeft.Y + backRight.Y)) * 0.5f;
            var thickness = Mathf.Max(2f, Mathf.Abs(heightSpan) * 0.12f);
            _sidePolygon.Polygon = new[]
            {
                frontLeft,
                frontRight,
                new Vector2(frontRight.X, frontRight.Y + thickness),
                new Vector2(frontLeft.X, frontLeft.Y + thickness),
            };
        }

        if (_collShape != null)
        {
            // ConvexPolygonShape2D 接受梯形；polygon 須為凸
            var shape = new ConvexPolygonShape2D { Points = quad };
            _collShape.Shape = shape;
        }
    }

    private void OnMouseEntered()
    {
        _isHovered = true;
        UpdateModulate();
    }

    private void OnMouseExited()
    {
        _isHovered = false;
        UpdateModulate();
    }

    private void OnAreaInput(Node viewport, InputEvent @event, long shapeIdx)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            EmitSignal(SignalName.TileClicked, Row, Col);
        }
    }

    private void UpdateOverlay()
    {
        if (_overlayPolygon is null) return;
        switch (_overlayKind)
        {
            case OverlayKind.None:
                _overlayPolygon.Visible = false;
                break;
            case OverlayKind.LegalPlacement:
                _overlayPolygon.Color = new Color(0.4f, 1.0f, 0.4f, 0.35f);
                _overlayPolygon.Visible = true;
                break;
            case OverlayKind.MoveTarget:
                _overlayPolygon.Color = new Color(1.0f, 0.85f, 0.2f, 0.35f);
                _overlayPolygon.Visible = true;
                break;
            case OverlayKind.PlayerMark:
                _overlayPolygon.Color = new Color(1.0f, 0.3f, 0.3f, 0.30f);
                _overlayPolygon.Visible = true;
                break;
        }
    }

    private void ApplyTexture()
    {
        if (_topPolygon is null) return;
        if (!_isPlaced)
        {
            _topPolygon.Texture = null;
            _topPolygon.Color = new Color(1, 1, 1, 0); // 完全透明，但保留 click area
            return;
        }
        var path = _isExplored && TerrainTexturePaths.TryGetValue(_terrain, out var p)
            ? p
            : CardBackPath;
        _topPolygon.Texture = ResourceLoader.Load<Texture2D>(path);
        _topPolygon.Color = Colors.White;
        UpdateTopPolygonUv();
        UpdateModulate();
    }

    /// <summary>
    /// 把 4 角 polygon 對應到 texture 的 4 個像素角。
    /// Polygon2D.Uv 是「texture 像素座標」，不是 normalized [0,1]，
    /// 所以要乘上 texture size。順序：back-left=(0,0), back-right=(W,0),
    /// front-right=(W,H), front-left=(0,H) — PNG 上半 illustration 對到地塊後緣。
    /// </summary>
    private void UpdateTopPolygonUv()
    {
        if (_topPolygon is null) return;
        if (_topPolygon.Texture is null)
        {
            _topPolygon.UV = System.Array.Empty<Vector2>();
            return;
        }
        var texSize = _topPolygon.Texture.GetSize();
        _topPolygon.UV = new[]
        {
            new Vector2(0, 0),
            new Vector2(texSize.X, 0),
            new Vector2(texSize.X, texSize.Y),
            new Vector2(0, texSize.Y),
        };
    }

    private void UpdateModulate()
    {
        if (_topPolygon is null) return;
        _topPolygon.Modulate = _isHovered ? new Color(1.25f, 1.25f, 1.25f) : Colors.White;
    }
}
