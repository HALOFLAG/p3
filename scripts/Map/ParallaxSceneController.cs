using CardNarrative.Core.Map;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Map;

/// <summary>
/// 場景立繪三層視差（任務 8 / 規格書 §2.2）— Plan B 擬 3D 立體劇場版本。
///
/// Stage 1（本檔交付）：
/// - 地板（Polygon2D）= 玩家地塊的 4 角 quad，**整體向上偏移**讓盒子漂浮在地塊上方有空隙
/// - 後牆（Polygon2D）站在地板後緣上，貼**完整**地塊卡 PNG
/// - **後牆厚度**：上緣加一條深色帶模擬板厚
/// - **地板厚度**：前緣下方加一條深色帶模擬板厚（slab side）
///
/// 後續 Stage 加：側牆三角 + 角色 sprite + 滑鼠視差 + 動態效果 + 切換淡入淡出
/// </summary>
public partial class ParallaxSceneController : Node2D
{
    /// <summary>後牆高度相對地板後緣寬度的比例。</summary>
    public const float WallHeightRatio = 1.4f;

    /// <summary>整體向上漂浮量（相對地板後緣寬度的比例）— 在玩家地塊與立繪盒之間留空隙。</summary>
    public const float FloatGapRatio = 0.20f;

    /// <summary>地板「板厚」相對地板深度（前後 Y 差）的比例。</summary>
    public const float FloorThicknessRatio = 0.10f;

    /// <summary>
    /// 地板後緣位置：tile front → tile back 的比例。後緣 = 後牆站立位置。
    /// </summary>
    public const float FloorBackEdgeRatio = 0.5f;

    /// <summary>
    /// 地板前緣位置：tile front → tile back 的比例。0.1 = 前緣朝後縮 10%（地塊深度單位）。
    /// 地板深度 = Back - Front = 0.6。
    /// </summary>
    public const float FloorFrontEdgeRatio = 0.1f;

    /// <summary>角色高度相對後牆高度的比例（站在地板上）。0.4875 = 上一版 0.325 × 150%。</summary>
    public const float CharacterHeightRatio = 0.4875f;

    /// <summary>前景高度相對後牆高度的比例（前景立起來，像是劇場前舞台板）。</summary>
    public const float ForegroundHeightRatio = 0.55f;

    /// <summary>角色橫向間距相對地板前緣寬度的比例（兩角色中心離地板中心的距離）。</summary>
    public const float CharacterSpacingRatio = 0.14f;

    private const string CharacterAPath = "res://docs/人物A.png";
    private const string CharacterBPath = "res://docs/人物B.png";
    private const string ForegroundPath = "res://docs/前景.png";

    private Polygon2D? _backWall;
    private Polygon2D? _floor;
    private Polygon2D? _floorFrontEdge;   // 地板前緣厚度
    private Polygon2D? _floorLeftEdge;    // 地板左斜邊厚度
    private Polygon2D? _floorRightEdge;   // 地板右斜邊厚度
    private Polygon2D? _character1;       // 角色 A（左）
    private Polygon2D? _character2;       // 角色 B（右）
    private Polygon2D? _foreground;       // 前景圖（覆蓋地板前緣）
    private MapTerrain _currentTerrain = (MapTerrain)(-1);

    private static readonly System.Collections.Generic.Dictionary<MapTerrain, string> TerrainTextures = new()
    {
        { MapTerrain.Forest,   "res://art/tiles/forest.png"   },
        { MapTerrain.Path,     "res://art/tiles/path.png"     },
        { MapTerrain.Grass,    "res://art/tiles/grass.png"    },
        { MapTerrain.Water,    "res://art/tiles/water.png"    },
        { MapTerrain.Mountain, "res://art/tiles/mountain.png" },
        { MapTerrain.Building, "res://art/tiles/building.png" },
    };

    public override void _Ready()
    {
        // 繪製順序（先加 = 先畫，會在底層）：
        // 1. 後牆（最底，遠景 far）
        // 2. 地板側面厚度三條（在地板下方，模擬 slab side）
        // 3. 地板（蓋住厚度帶上緣）
        // 4. 角色 mid 層（站在地板上，紅+綠 placeholder）
        // 5. 前景裝飾 near 層（floor 前緣，粉+白+黃 placeholder）

        _backWall = new Polygon2D { Name = "BackWall" };
        AddChild(_backWall);
        _backWall.Color = Colors.White;

        _floorLeftEdge = new Polygon2D { Name = "FloorLeftEdge", Color = FloorEdgeColor };
        AddChild(_floorLeftEdge);
        _floorRightEdge = new Polygon2D { Name = "FloorRightEdge", Color = FloorEdgeColor };
        AddChild(_floorRightEdge);
        _floorFrontEdge = new Polygon2D { Name = "FloorFrontEdge", Color = FloorEdgeColor };
        AddChild(_floorFrontEdge);

        _floor = new Polygon2D { Name = "Floor", Color = Palette.Brown };
        AddChild(_floor);

        // 角色 mid 層（人物A.png / 人物B.png）
        _character1 = new Polygon2D
        {
            Name = "CharacterA",
            Color = Colors.White,
            Texture = ResourceLoader.Load<Texture2D>(CharacterAPath),
        };
        AddChild(_character1);
        _character2 = new Polygon2D
        {
            Name = "CharacterB",
            Color = Colors.White,
            Texture = ResourceLoader.Load<Texture2D>(CharacterBPath),
        };
        AddChild(_character2);

        // 前景 near 層（前景.png，地板前緣橫排）
        _foreground = new Polygon2D
        {
            Name = "Foreground",
            Color = Colors.White,
            Texture = ResourceLoader.Load<Texture2D>(ForegroundPath),
        };
        AddChild(_foreground);
    }

    /// <summary>由 MainMapRenderer 在玩家移動 / camera offset 變化時呼叫。</summary>
    public void SetTileQuad(Vector2 backLeft, Vector2 backRight, Vector2 frontRight, Vector2 frontLeft)
    {
        // === 0. 縮短地板深度：兩條 lerp，分別決定後緣 / 前緣的深度位置 ===
        // 後緣 0.6（與舊版相同 → 後牆站立位置不變），前緣 0.2（朝後縮 20%）→ 地板深度 40%。
        var origBackLeft   = backLeft;
        var origBackRight  = backRight;
        var origFrontLeft  = frontLeft;
        var origFrontRight = frontRight;

        backLeft   = origFrontLeft.Lerp(origBackLeft,   FloorBackEdgeRatio);
        backRight  = origFrontRight.Lerp(origBackRight, FloorBackEdgeRatio);
        frontLeft  = origFrontLeft.Lerp(origBackLeft,   FloorFrontEdgeRatio);
        frontRight = origFrontRight.Lerp(origBackRight, FloorFrontEdgeRatio);

        // === 1. 整體向上漂浮（與地塊之間留空隙） ===
        var backEdgeWidth = backRight.X - backLeft.X;
        var floatGap = new Vector2(0, -backEdgeWidth * FloatGapRatio);
        backLeft += floatGap;
        backRight += floatGap;
        frontRight += floatGap;
        frontLeft += floatGap;

        // === 2. 地板（梯形）===
        if (_floor != null)
        {
            _floor.Polygon = new[] { backLeft, backRight, frontRight, frontLeft };
        }

        // === 3. 地板三面厚度（前 / 左斜 / 右斜）===
        var floorDepth = ((frontLeft.Y + frontRight.Y) - (backLeft.Y + backRight.Y)) * 0.5f;
        var floorThick = floorDepth * FloorThicknessRatio;
        var down = new Vector2(0, floorThick);

        if (_floorFrontEdge != null)
        {
            _floorFrontEdge.Polygon = new[]
            {
                frontLeft,
                frontRight,
                frontRight + down,
                frontLeft + down,
            };
        }
        if (_floorLeftEdge != null)
        {
            _floorLeftEdge.Polygon = new[]
            {
                backLeft,
                frontLeft,
                frontLeft + down,
                backLeft + down,
            };
        }
        if (_floorRightEdge != null)
        {
            _floorRightEdge.Polygon = new[]
            {
                backRight,
                frontRight,
                frontRight + down,
                backRight + down,
            };
        }

        // === 4. 後牆（站在地板後緣上）===
        var wallHeight = backEdgeWidth * WallHeightRatio;
        var wallTopLeft = new Vector2(backLeft.X, backLeft.Y - wallHeight);
        var wallTopRight = new Vector2(backRight.X, backRight.Y - wallHeight);
        if (_backWall != null)
        {
            _backWall.Polygon = new[] { wallTopLeft, wallTopRight, backRight, backLeft };
            UpdateBackWallUv();
        }

        // === 5. 角色 mid 層（人物A 左、人物B 右，使用 texture 原始長寬比）===
        var floorFrontWidth = frontRight.X - frontLeft.X;
        var floorCenterX = (backLeft.X + backRight.X + frontLeft.X + frontRight.X) * 0.25f;
        var floorMidY = (backLeft.Y + backRight.Y + frontLeft.Y + frontRight.Y) * 0.25f;
        var charHeight = wallHeight * CharacterHeightRatio;
        var charSpacing = floorFrontWidth * CharacterSpacingRatio;

        SetCharacterPolygon(_character1, floorCenterX - charSpacing, floorMidY, charHeight);
        SetCharacterPolygon(_character2, floorCenterX + charSpacing, floorMidY, charHeight);

        // === 6. 前景圖（立起來的矩形，bottom = 地板前緣，top 垂直往上 fgHeight；
        //      就像劇場舞台前緣的小擋板，整面立起來貼前景圖）===
        if (_foreground != null && _foreground.Texture != null)
        {
            var fgHeight = wallHeight * ForegroundHeightRatio;
            var fgTopLeft = new Vector2(frontLeft.X, frontLeft.Y - fgHeight);
            var fgTopRight = new Vector2(frontRight.X, frontRight.Y - fgHeight);
            _foreground.Polygon = new[]
            {
                fgTopLeft,
                fgTopRight,
                frontRight,
                frontLeft,
            };
            _foreground.UV = MakeFullTextureUv(_foreground.Texture.GetSize());
        }
    }

    /// <summary>角色 polygon 設定：用 texture 的原始長寬比決定 polygon 寬度。</summary>
    private static void SetCharacterPolygon(Polygon2D? polygon, float centerX, float bottomY, float height)
    {
        if (polygon == null) return;
        if (polygon.Texture == null)
        {
            polygon.Polygon = MakeRect(centerX, bottomY, height * 0.4f, height); // fallback 比例
            return;
        }
        var texSize = polygon.Texture.GetSize();
        var width = height * (texSize.X / texSize.Y); // 維持原圖長寬比
        polygon.Polygon = MakeRect(centerX, bottomY, width, height);
        polygon.UV = MakeFullTextureUv(texSize);
    }

    /// <summary>UV 對映：4 角 → 完整 texture (0..W, 0..H)。順序：top-left → top-right → bottom-right → bottom-left。</summary>
    private static Vector2[] MakeFullTextureUv(Vector2 textureSize) => new[]
    {
        new Vector2(0, 0),
        new Vector2(textureSize.X, 0),
        new Vector2(textureSize.X, textureSize.Y),
        new Vector2(0, textureSize.Y),
    };

    /// <summary>給定中心 X / 底部 Y / 寬高，產生矩形 4 角（順時針，feet=底）。</summary>
    private static Vector2[] MakeRect(float centerX, float bottomY, float width, float height)
    {
        var halfW = width * 0.5f;
        return new[]
        {
            new Vector2(centerX - halfW, bottomY - height), // top-left
            new Vector2(centerX + halfW, bottomY - height), // top-right
            new Vector2(centerX + halfW, bottomY),          // bottom-right
            new Vector2(centerX - halfW, bottomY),          // bottom-left
        };
    }

    public void SetTerrain(MapTerrain terrain)
    {
        if (_currentTerrain == terrain) return;
        _currentTerrain = terrain;
        UpdateBackWallTexture();
    }

    private void UpdateBackWallTexture()
    {
        if (_backWall == null) return;
        if (!TerrainTextures.TryGetValue(_currentTerrain, out var path))
        {
            _backWall.Texture = null;
            return;
        }
        var tex = ResourceLoader.Load<Texture2D>(path);
        if (tex == null) return;
        _backWall.Texture = tex;
        UpdateBackWallUv();
    }

    /// <summary>UV 對映：4 角 → 完整 texture (0..W, 0..H)，貼整張地塊卡。</summary>
    private void UpdateBackWallUv()
    {
        if (_backWall?.Texture == null) return;
        var size = _backWall.Texture.GetSize();
        _backWall.UV = new[]
        {
            new Vector2(0, 0),
            new Vector2(size.X, 0),
            new Vector2(size.X, size.Y),
            new Vector2(0, size.Y),
        };
    }

    // 厚度帶用較深的 Brown，給「立體陰影」感
    private static readonly Color FloorEdgeColor = new(0.25f, 0.15f, 0.08f, 1f);
}
