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

    /// <summary>側緣厚度相對前後 Y 跨距的比例。</summary>
    private const float ThicknessRatio = 0.08f;

    private Polygon2D? _topPolygon;        // 地形美術 + UV
    private Polygon2D? _frontSidePolygon;  // 前緣下方厚度
    private Polygon2D? _leftSidePolygon;   // 左斜邊厚度
    private Polygon2D? _rightSidePolygon;  // 右斜邊厚度
    private Label? _nameLabel;             // Phase 2：卡面上方中文卡名（quick visual aid，方便 L2 手測辨識）
    private Polygon2D? _overlayPolygon;    // 半透明色蓋層（LegalPlacement / MoveTarget / PlayerMark）
    private Polygon2D? _pulsePolygon;      // Task 10：小地圖點擊時短暫脈動高亮（與 _overlayPolygon 獨立）
    private Tween? _pulseTween;
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

        // 側緣 3 面（左/右/前下方厚度，畫在 top 之下，給「slab 板厚」立體感）
        var sideColor = new Color(0.169f, 0.114f, 0.055f, 1f);
        _leftSidePolygon = new Polygon2D { Name = "LeftSidePolygon", Color = sideColor, Visible = false };
        AddChild(_leftSidePolygon);
        _rightSidePolygon = new Polygon2D { Name = "RightSidePolygon", Color = sideColor, Visible = false };
        AddChild(_rightSidePolygon);
        _frontSidePolygon = new Polygon2D { Name = "FrontSidePolygon", Color = sideColor, Visible = false };
        AddChild(_frontSidePolygon);

        // 地形主面
        _topPolygon = new Polygon2D
        {
            Name = "TopPolygon",
            Color = Colors.White,
        };
        AddChild(_topPolygon);

        // 卡名 label：白字黑邊，置於卡面上方（後緣內側）。
        // SetTileQuad 會根據後緣寬度設定 Position/Size — 與卡面同寬。
        _nameLabel = new Label
        {
            Name = "NameLabel",
            Text = string.Empty,
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _nameLabel.AddThemeColorOverride("font_color", Colors.White);
        _nameLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _nameLabel.AddThemeConstantOverride("outline_size", 4);
        _nameLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_nameLabel);

        // Overlay 蓋層
        _overlayPolygon = new Polygon2D
        {
            Name = "OverlayPolygon",
            Visible = false,
        };
        AddChild(_overlayPolygon);

        // Task 10 — 脈動高亮層（小地圖點擊觸發；與 OverlayPolygon 獨立避免覆蓋 PlayerMark）
        _pulsePolygon = new Polygon2D
        {
            Name = "PulsePolygon",
            Color = new Color(1.0f, 0.85f, 0.2f, 0.35f),
            Visible = false,
            Modulate = new Color(1f, 1f, 1f, 0f),
        };
        AddChild(_pulsePolygon);

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
        if (_frontSidePolygon != null) _frontSidePolygon.Visible = isPlaced;
        if (_leftSidePolygon != null) _leftSidePolygon.Visible = isPlaced;
        if (_rightSidePolygon != null) _rightSidePolygon.Visible = isPlaced;
        if (_nameLabel != null) _nameLabel.Visible = isPlaced && !string.IsNullOrEmpty(_nameLabel.Text);
    }

    /// <summary>
    /// Phase 2 quick visual aid：設定卡名顯示文字。
    /// MainMapRenderer 從 BackingModule.Tiles[id].Name 反查後傳入；null/空字串 → 隱藏 label。
    /// 顯示條件：tile 已 placed（即使 card-back 未探索也顯示，因 RightPanel slot1 在持有時已暴露名）。
    /// </summary>
    public void SetTileName(string? name)
    {
        if (_nameLabel is null) return;
        _nameLabel.Text = name ?? string.Empty;
        _nameLabel.Visible = _isPlaced && !string.IsNullOrEmpty(_nameLabel.Text);
    }

    public void SetOverlay(OverlayKind kind)
    {
        _overlayKind = kind;
        UpdateOverlay();
    }

    /// <summary>
    /// Task 10 — 觸發短暫脈動高亮（小地圖點擊用）。
    /// 動畫時序：淡入 200ms → hold 600ms → 淡出 200ms（總長 1s）。
    /// 重複呼叫會殺掉前一次 Tween 重啟。
    /// </summary>
    public void TriggerPulseHighlight()
    {
        if (_pulsePolygon is null) return;
        _pulseTween?.Kill();
        _pulsePolygon.Visible = true;
        _pulsePolygon.Modulate = new Color(1f, 1f, 1f, 0f);
        var tween = CreateTween();
        tween.TweenProperty(_pulsePolygon, "modulate:a", 1f, 0.2);
        tween.TweenInterval(0.6);
        tween.TweenProperty(_pulsePolygon, "modulate:a", 0f, 0.2);
        tween.TweenCallback(Callable.From(() =>
        {
            if (_pulsePolygon != null) _pulsePolygon.Visible = false;
        }));
        _pulseTween = tween;
    }

    /// <summary>
    /// Phase 2 任務 11 Stage 4 — 觸發 transformTile 翻牌動畫（規格書 §1.5 / §2.4）。
    /// 沿用 _pulsePolygon 但用「閃爍」而非脈動：
    ///   1) 短促閃白（modulate.a 0→1 60ms）
    ///   2) flicker：瞬間黯淡（a 1→0.3 60ms）→ 再亮（0.3→1 60ms）
    ///   3) 淡出 (a 1→0 200ms)
    /// 視覺上比 PulseHighlight 更「事件性」，提示玩家：這格剛被改變了。
    /// 與 TileChanged 觸發的 ApplyTexture（紋理切換）順序：
    ///   priority TileChanged(10) 先 → 紋理已換新 → TileTransformed(70) 觸發本動畫。
    /// </summary>
    public void TriggerTransformAnimation()
    {
        if (_pulsePolygon is null) return;
        _pulseTween?.Kill();
        _pulsePolygon.Visible = true;
        _pulsePolygon.Modulate = new Color(1f, 1f, 1f, 0f);
        var tween = CreateTween();
        // 閃白
        tween.TweenProperty(_pulsePolygon, "modulate:a", 1f, 0.06);
        // flicker：暗下去 → 再亮一次（一個 100ms 內的閃爍）
        tween.TweenProperty(_pulsePolygon, "modulate:a", 0.3f, 0.06);
        tween.TweenProperty(_pulsePolygon, "modulate:a", 1f, 0.06);
        // 淡出
        tween.TweenProperty(_pulsePolygon, "modulate:a", 0f, 0.2);
        tween.TweenCallback(Callable.From(() =>
        {
            if (_pulsePolygon != null) _pulsePolygon.Visible = false;
        }));
        _pulseTween = tween;
    }

    /// <summary>
    /// 設定地塊 4 角（在 TileVisual 的 local 座標系，原點為 TileVisual.Position）。
    /// 順序：後左 → 後右 → 前右 → 前左（順時針）。
    /// </summary>
    public void SetTileQuad(Vector2 backLeft, Vector2 backRight, Vector2 frontRight, Vector2 frontLeft)
    {
        // 4 corner 直接保留原投影尺寸；地塊間隙由 MainMapRenderer 透過 node.Position
        // 額外 push-apart offset 處理（避免縮小 polygon 導致 PNG 美術也被縮）。
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

        if (_pulsePolygon != null)
        {
            _pulsePolygon.Polygon = quad;
        }

        // 卡名 label：與卡面後緣同寬，貼在後緣內側（卡面上方）
        if (_nameLabel != null)
        {
            const float labelHeight = 22f;
            var width = backRight.X - backLeft.X;
            _nameLabel.Position = new Vector2(backLeft.X, backLeft.Y);
            _nameLabel.Size = new Vector2(width, labelHeight);
        }

        // === 2. 三面側緣厚度（左/右/前下方）===
        var heightSpan = ((frontLeft.Y + frontRight.Y) - (backLeft.Y + backRight.Y)) * 0.5f;
        var thickness = Mathf.Max(2f, Mathf.Abs(heightSpan) * ThicknessRatio);
        var down = new Vector2(0, thickness);

        if (_frontSidePolygon != null)
        {
            // 前緣下方厚度（梯形寬邊）
            _frontSidePolygon.Polygon = new[]
            {
                frontLeft,
                frontRight,
                frontRight + down,
                frontLeft + down,
            };
        }

        if (_leftSidePolygon != null)
        {
            // 左斜邊（從 backLeft 到 frontLeft）下方厚度，平行四邊形
            _leftSidePolygon.Polygon = new[]
            {
                backLeft,
                frontLeft,
                frontLeft + down,
                backLeft + down,
            };
        }

        if (_rightSidePolygon != null)
        {
            // 右斜邊（從 backRight 到 frontRight）下方厚度
            _rightSidePolygon.Polygon = new[]
            {
                backRight,
                frontRight,
                frontRight + down,
                backRight + down,
            };
        }

        if (_collShape != null)
        {
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
