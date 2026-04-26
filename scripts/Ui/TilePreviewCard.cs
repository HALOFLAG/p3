using CardNarrative.Core.Map;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 平面地塊預覽卡（NEXT 預覽 / 圖鑑等用途）。
///
/// 與 TileVisual 不同：TileVisual 用 Polygon2D + UV 做透視梯形（in-map 渲染）；
/// 本元件為平面正方形 PanelContainer，純展示 PNG + 名稱，不做透視。
///
/// 使用：
///   var card = new TilePreviewCard();
///   AddChild(card);
///   card.SetTerrain(MapTerrain.Forest);  // 顯示 forest.png + "森林"
///   card.SetTerrain(null);                // 空槽（虛線框 + "—"）
/// </summary>
public partial class TilePreviewCard : PanelContainer
{
    public const int CardSize = 100;     // 正方形 100×100

    private TextureRect? _texture;
    private MapTerrain? _terrain;
    private bool _highlighted;

    private static readonly System.Collections.Generic.Dictionary<MapTerrain, string> TerrainTexturePaths = new()
    {
        { MapTerrain.Forest,   "res://art/tiles/forest.png"   },
        { MapTerrain.Path,     "res://art/tiles/path.png"     },
        { MapTerrain.Grass,    "res://art/tiles/grass.png"    },
        { MapTerrain.Water,    "res://art/tiles/water.png"    },
        { MapTerrain.Mountain, "res://art/tiles/mountain.png" },
        { MapTerrain.Building, "res://art/tiles/building.png" },
    };

    public static string DisplayName(MapTerrain terrain) => terrain switch
    {
        MapTerrain.Forest   => "森林",
        MapTerrain.Path     => "道路",
        MapTerrain.Grass    => "草地",
        MapTerrain.Water    => "水域",
        MapTerrain.Mountain => "山地",
        MapTerrain.Building => "建築",
        _ => terrain.ToString(),
    };

    public override void _Ready()
    {
        if (_texture != null) return; // idempotent

        CustomMinimumSize = new Vector2(CardSize, CardSize);
        AddThemeStyleboxOverride("panel", BuildFrameStyle(empty: true));
        MouseFilter = MouseFilterEnum.Ignore;

        _texture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_texture);
    }

    /// <summary>填入 terrain（null = 空槽）。</summary>
    public void SetTerrain(MapTerrain? terrain)
    {
        if (_texture == null) _Ready(); // 防 SetTerrain 在 _Ready 之前被呼叫

        _terrain = terrain;
        ApplyFrameStyle();

        if (terrain is { } t && _texture != null)
        {
            _texture.Texture = TerrainTexturePaths.TryGetValue(t, out var path)
                ? ResourceLoader.Load<Texture2D>(path)
                : null;
            TooltipText = DisplayName(t);
        }
        else
        {
            if (_texture != null) _texture.Texture = null;
            TooltipText = "";
        }
    }

    /// <summary>切換高亮（用於「玩家正持有此地塊」狀態）— 加亮金邊 + 暖光 modulate。</summary>
    public void SetHighlighted(bool highlighted)
    {
        if (_texture == null) _Ready();
        if (_highlighted == highlighted) return;
        _highlighted = highlighted;
        ApplyFrameStyle();
        Modulate = highlighted ? new Color(1.15f, 1.10f, 0.95f) : Colors.White;
    }

    private void ApplyFrameStyle()
    {
        AddThemeStyleboxOverride("panel", BuildFrameStyle(empty: _terrain is null, highlighted: _highlighted));
    }

    private static StyleBoxFlat BuildFrameStyle(bool empty, bool highlighted = false)
    {
        if (empty)
        {
            return new StyleBoxFlat
            {
                BgColor = Palette.WithAlpha(Palette.PaperLight, 0.4f),
                BorderColor = Palette.WithAlpha(Palette.InkLight, 0.5f),
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
                ContentMarginLeft = 2, ContentMarginRight = 2,
                ContentMarginTop = 2, ContentMarginBottom = 2,
            };
        }
        int border = highlighted ? 4 : 2;
        return new StyleBoxFlat
        {
            BgColor = highlighted ? Palette.PaperLight : Palette.Paper,
            BorderColor = Palette.Gold,
            BorderWidthLeft = border, BorderWidthRight = border,
            BorderWidthTop = border, BorderWidthBottom = border,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ShadowColor = highlighted
                ? Palette.WithAlpha(Palette.Gold, 0.55f)
                : Palette.WithAlpha(Palette.Ink, 0.3f),
            ShadowSize = highlighted ? 8 : 4,
            ShadowOffset = highlighted ? Vector2.Zero : new Vector2(2, 2),
            ContentMarginLeft = 2, ContentMarginRight = 2,
            ContentMarginTop = 2, ContentMarginBottom = 2,
        };
    }
}
