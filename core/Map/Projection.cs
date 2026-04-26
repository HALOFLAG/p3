namespace CardNarrative.Core.Map;

/// <summary>
/// Phase 1 Task 2 — 單點透視投影純函式（無 Godot 依賴，可單元測試）。
/// 對應規格書 §5.1.1 / §5.1.2。
///
/// 投影規則：
/// - 視野為 5×5（上下對稱），玩家位於中央 (relRow=0, relCol=0)
/// - relRow = -2 為最遠（螢幕頂、消失點附近）；relRow = +2 為最近（螢幕底）
/// - depthIndex = relRow + 2，0 = 最遠，4 = 最近
/// - 規格書 §5.1.2：t = depthIndex / maxDepth；y = vpY + (groundY - vpY) × t²
///   其中 t=0 → y=vpY（頂部，消失點上）；t=1 → y=groundY（底部，地面）
/// - scale 隨 t 線性變化：t=0（遠）為 farScale；t=1（近）為 1.0
/// - 寬高同步縮放（地塊本身為正方形）
///
/// 兩種投影：
/// 1. <see cref="Project"/> — 直立卡（軸對齊正方形 rect），舊行為，相容測試
/// 2. <see cref="ProjectQuad"/> — 地面梯形（4 角 quad），新傾斜地板渲染
/// </summary>
public static class Projection
{
    /// <summary>規格書 §5.1.1 預設參數（7×7 視野 + 強透視 + linear Y）。</summary>
    /// <remarks>
    /// 1:1 條件（player row）：yRange = BaseTileSize × ((1+FarScale)/2) × visibleRows
    ///         = 70 × 0.775 × 7 = 379.75 → 取 380 (vpY=220, groundY=600)
    /// FarScale=0.55 → 遠列寬度 ~57% 近列，符合明顯傾斜的桌面視角。
    /// </remarks>
    public static readonly ProjectionParams Default = new(
        ViewWidth: 560f,
        ViewHeight: 600f,
        VanishingPointY: 220f,
        GroundY: 600f,
        BaseTileSize: 70f,
        FarScale: 0.55f,       // 強透視（遠列縮 ~45%）→ 明顯傾斜的桌面感
        VisibleRows: 7,
        VisibleCols: 7);

    /// <summary>計算單一可見地塊在 viewBox 中的螢幕座標（top-left）與大小。</summary>
    /// <param name="relRow">玩家為原點的相對 row：-2 (最遠) ~ +2 (最近)</param>
    /// <param name="relCol">玩家為原點的相對 col：-2 (左) ~ +2 (右)</param>
    /// <param name="p">投影參數</param>
    public static ProjectedTile Project(int relRow, int relCol, ProjectionParams p)
    {
        var (yCenter, scale) = DepthSlice(relRow, p);
        var tileSize = p.BaseTileSize * scale;
        var xCenter = p.ViewWidth * 0.5f + relCol * tileSize;

        var halfRows = (p.VisibleRows - 1) / 2f;
        var t = (relRow + halfRows + 0.5f) / p.VisibleRows;

        return new ProjectedTile(
            X: xCenter - tileSize * 0.5f,
            Y: yCenter - tileSize * 0.5f,
            Width: tileSize,
            Height: tileSize,
            Scale: scale,
            T: t);
    }

    /// <summary>計算單一地塊以「地面 1×1 單位」投影出的 4 角梯形（後緣窄、前緣寬）。</summary>
    /// <param name="relRow">玩家為原點的相對 row：-2 (最遠) ~ +2 (最近)</param>
    /// <param name="relCol">玩家為原點的相對 col：-2 (左) ~ +2 (右)</param>
    /// <param name="p">投影參數</param>
    public static ProjectedQuad ProjectQuad(int relRow, int relCol, ProjectionParams p)
    {
        // 後緣（深度 = relRow - 0.5）與前緣（深度 = relRow + 0.5）
        var (yBack, scaleBack) = DepthSliceContinuous(relRow - 0.5f, p);
        var (yFront, scaleFront) = DepthSliceContinuous(relRow + 0.5f, p);
        var widthBack = p.BaseTileSize * scaleBack;
        var widthFront = p.BaseTileSize * scaleFront;

        // 對於 x：center = viewW/2 + relCol × widthAtDepth；left/right edges 各偏移 ±widthAtDepth/2
        var centerXBack = p.ViewWidth * 0.5f + relCol * widthBack;
        var centerXFront = p.ViewWidth * 0.5f + relCol * widthFront;

        var backLeft = new Point2(centerXBack - widthBack * 0.5f, yBack);
        var backRight = new Point2(centerXBack + widthBack * 0.5f, yBack);
        var frontRight = new Point2(centerXFront + widthFront * 0.5f, yFront);
        var frontLeft = new Point2(centerXFront - widthFront * 0.5f, yFront);

        var center = new Point2(
            (backLeft.X + backRight.X + frontRight.X + frontLeft.X) * 0.25f,
            (backLeft.Y + backRight.Y + frontRight.Y + frontLeft.Y) * 0.25f);

        return new ProjectedQuad(backLeft, backRight, frontRight, frontLeft, center);
    }

    /// <summary>判斷某 (relRow, relCol) 是否在 5×5 可見範圍。</summary>
    public static bool IsVisible(int relRow, int relCol, ProjectionParams p)
    {
        var halfRows = p.VisibleRows / 2; // 5 → 2
        var halfCols = p.VisibleCols / 2; // 5 → 2
        return relRow >= -halfRows && relRow <= halfRows
            && relCol >= -halfCols && relCol <= halfCols;
    }

    /// <summary>整數深度（row 中心）→ (Y 中心, scale)。</summary>
    private static (float Y, float Scale) DepthSlice(int relRow, ProjectionParams p)
    {
        // 新 t 公式：每列佔 1/visibleRows 的 t 範圍 → 最遠列後緣 t=0 (=vpY)，最近列前緣 t=1 (=groundY)
        // → 整個 7×7 視野剛好填滿 [vpY, groundY]，不會超出畫面。
        var halfRows = (p.VisibleRows - 1) / 2f;
        var t = (relRow + halfRows + 0.5f) / p.VisibleRows;
        return Slice(t, p);
    }

    /// <summary>連續深度（可為 row ± 0.5 等小數）→ (Y, scale)，給 ProjectQuad 用。</summary>
    private static (float Y, float Scale) DepthSliceContinuous(float relRow, ProjectionParams p)
    {
        var halfRows = (p.VisibleRows - 1) / 2f;
        var t = (relRow + halfRows + 0.5f) / p.VisibleRows;
        return Slice(t, p);
    }

    private static (float Y, float Scale) Slice(float t, ProjectionParams p)
    {
        // 幾何尺度（exponential）+ 對應 Y：scale(t) = farScale × (1/farScale)^t
        // → scale 與 Y 都是線性相關（dxCenter/dyCenter 為常數）→ 同欄列邊緣在螢幕上呈直線
        // → ∫₀ᵗ scale 對 ∫₀¹ scale 標準化後，dY/dt ∝ scale(t)，每列 Δy 與寬度同步 → 每列 1:1
        //
        // 推導：scale(t) = farScale × exp(ln(1/farScale) × t)
        //       積分形式 normalized = (scale(t) - farScale) / (1 - farScale)
        //       （非常乾淨，避開 ln/exp 在公式中出現）
        var farScale = p.FarScale;
        var scaleAtT = farScale * (float)System.Math.Pow(1.0 / farScale, t);
        var normalizedY = (scaleAtT - farScale) / (1f - farScale);
        var y = p.VanishingPointY + (p.GroundY - p.VanishingPointY) * normalizedY;
        return (y, scaleAtT);
    }
}

/// <summary>投影參數（規格書 §5.1.1 各欄位的具名版）。</summary>
public readonly record struct ProjectionParams(
    float ViewWidth,
    float ViewHeight,
    float VanishingPointY,
    float GroundY,
    float BaseTileSize,
    float FarScale,
    int VisibleRows,
    int VisibleCols);

/// <summary>單一地塊投影結果（直立卡，rect）。座標為 viewBox 內 top-left。</summary>
public readonly record struct ProjectedTile(
    float X,
    float Y,
    float Width,
    float Height,
    float Scale,
    float T);

/// <summary>輕量 2D 點（無 Godot 依賴，給 core 用）。</summary>
public readonly record struct Point2(float X, float Y);

/// <summary>地面梯形 4 角（後窄前寬），順序：後左 → 後右 → 前右 → 前左。</summary>
public readonly record struct ProjectedQuad(
    Point2 BackLeft,
    Point2 BackRight,
    Point2 FrontRight,
    Point2 FrontLeft,
    Point2 Center);
