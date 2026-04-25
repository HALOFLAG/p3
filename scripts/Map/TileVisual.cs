using CardNarrative.Core.Map;
using Godot;

namespace HauntedManor.Scripts.Map;

/// <summary>
/// Phase 1 Task 2/4/5 — 單一地塊節點（規格書 §5.1.3 / §5.1.4）。
/// 負責：紋理切換、hover 高亮、click 事件外送、合法區/移動目標 overlay。
/// 由 MainMapRenderer instance、定位、設定狀態。
/// </summary>
public partial class TileVisual : Node2D
{
    /// <summary>地塊被點擊。Payload：(row, col)。</summary>
    [Signal]
    public delegate void TileClickedEventHandler(int row, int col);

    private Sprite2D? _topSprite;
    private ColorRect? _sideFace;
    private Area2D? _hoverArea;
    private ColorRect? _overlay;

    private MapTerrain _terrain;
    private bool _isPlaced;
    private bool _isExplored;
    private bool _isHovered;
    private OverlayKind _overlayKind = OverlayKind.None;

    public int Row { get; set; }
    public int Col { get; set; }

    public enum OverlayKind { None, LegalPlacement, MoveTarget, PlayerMark }

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
        _topSprite = GetNode<Sprite2D>("TopSprite");
        _sideFace = GetNode<ColorRect>("SideFace");
        _hoverArea = GetNode<Area2D>("HoverArea");

        // 修 Bug 2 part A：複製 RectangleShape2D 避免 81 個 instance 共用同一個 SubResource
        var collShape = _hoverArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        if (collShape?.Shape != null)
        {
            collShape.Shape = (Shape2D)collShape.Shape.Duplicate();
        }

        _hoverArea.MouseEntered += OnMouseEntered;
        _hoverArea.MouseExited += OnMouseExited;
        _hoverArea.InputEvent += OnAreaInput;

        EnsureOverlay();
        ApplyTexture();
    }

    /// <summary>由 MainMapRenderer 在 layout 時呼叫。</summary>
    public void SetTile(MapTerrain terrain, bool isPlaced, bool isExplored)
    {
        _terrain = terrain;
        _isPlaced = isPlaced;
        _isExplored = isExplored;
        if (_topSprite != null) ApplyTexture();
    }

    public void SetOverlay(OverlayKind kind)
    {
        _overlayKind = kind;
        UpdateOverlay();
    }

    public void SetTileSize(float size)
    {
        var halfSize = size * 0.5f;

        // 修 Bug 1：sprite 紋理縮放只在有紋理時做；其餘元素的 size 設定都應該照常執行
        if (_topSprite?.Texture != null)
        {
            var textureSize = _topSprite.Texture.GetSize();
            var baseSize = Mathf.Max(textureSize.X, textureSize.Y);
            if (baseSize > 0f)
            {
                var scale = size / baseSize;
                _topSprite.Scale = new Vector2(scale, scale);
            }
        }

        if (_sideFace != null)
        {
            var sideHeight = size * 0.08f;
            _sideFace.OffsetLeft = -halfSize;
            _sideFace.OffsetRight = halfSize;
            _sideFace.OffsetTop = halfSize;
            _sideFace.OffsetBottom = halfSize + sideHeight;
            _sideFace.Visible = _isPlaced; // 未放格不顯示厚度
        }

        if (_overlay != null)
        {
            _overlay.OffsetLeft = -halfSize;
            _overlay.OffsetRight = halfSize;
            _overlay.OffsetTop = -halfSize;
            _overlay.OffsetBottom = halfSize;
        }

        // 修 Bug 2 part B：collision shape 同步縮放，與視覺一致避免重疊吃錯點擊
        if (_hoverArea != null)
        {
            var collShape = _hoverArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (collShape?.Shape is RectangleShape2D rect)
            {
                rect.Size = new Vector2(size, size);
            }
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
            GD.Print($"[TileVisual] click ({Row},{Col})");
            EmitSignal(SignalName.TileClicked, Row, Col);
        }
    }

    private void EnsureOverlay()
    {
        _overlay = GetNodeOrNull<ColorRect>("Overlay");
        if (_overlay != null) return;

        _overlay = new ColorRect
        {
            Name = "Overlay",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_overlay);
        UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        if (_overlay is null) return;
        switch (_overlayKind)
        {
            case OverlayKind.None:
                _overlay.Visible = false;
                break;
            case OverlayKind.LegalPlacement:
                _overlay.Color = new Color(0.4f, 1.0f, 0.4f, 0.35f); // 綠色透明
                _overlay.Visible = true;
                break;
            case OverlayKind.MoveTarget:
                _overlay.Color = new Color(1.0f, 0.85f, 0.2f, 0.35f); // 黃色透明
                _overlay.Visible = true;
                break;
            case OverlayKind.PlayerMark:
                _overlay.Color = new Color(1.0f, 0.3f, 0.3f, 0.30f); // 玩家位置紅
                _overlay.Visible = true;
                break;
        }
    }

    private void ApplyTexture()
    {
        if (_topSprite is null) return;
        if (!_isPlaced)
        {
            // 未放置格 → 完全透明（顯示空地，但保留 hover area 以便 MapExpand 可放置）
            _topSprite.Texture = null;
            return;
        }
        var path = _isExplored && TerrainTexturePaths.TryGetValue(_terrain, out var p)
            ? p
            : CardBackPath;
        _topSprite.Texture = ResourceLoader.Load<Texture2D>(path);
        UpdateModulate();
    }

    private void UpdateModulate()
    {
        if (_topSprite is null) return;
        _topSprite.Modulate = _isHovered ? new Color(1.25f, 1.25f, 1.25f) : Colors.White;
    }
}
