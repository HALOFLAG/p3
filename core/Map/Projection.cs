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
/// </summary>
public static class Projection
{
    /// <summary>規格書 §5.1.1 預設參數。</summary>
    public static readonly ProjectionParams Default = new(
        ViewWidth: 560f,
        ViewHeight: 400f,
        VanishingPointY: 130f,
        GroundY: 400f,
        BaseTileSize: 100f,    // 5×5 視野下單格 ~ 560/5 = 112，取整 100 留邊
        FarScale: 0.30f,       // 規格書 §5.1.1「漸進縮小至 ~30%」
        VisibleRows: 5,
        VisibleCols: 5);

    /// <summary>計算單一可見地塊在 viewBox 中的螢幕座標（top-left）與大小。</summary>
    /// <param name="relRow">玩家為原點的相對 row：-2 (最遠) ~ +2 (最近)</param>
    /// <param name="relCol">玩家為原點的相對 col：-2 (左) ~ +2 (右)</param>
    /// <param name="p">投影參數</param>
    public static ProjectedTile Project(int relRow, int relCol, ProjectionParams p)
    {
        // depthIndex: relRow=-2 (最遠) → 0；relRow=+2 (最近) → maxDepth
        var maxDepth = p.VisibleRows - 1; // 5 行 → maxDepth = 4
        var depthIndex = relRow + (maxDepth / 2);
        var t = (float)depthIndex / maxDepth; // 0..1

        // y_center per row（規格書 §5.1.2 公式）
        var yCenter = p.VanishingPointY + (p.GroundY - p.VanishingPointY) * (t * t);

        // scale: t=0 (遠) = farScale；t=1 (近) = 1.0
        var scale = p.FarScale + (1f - p.FarScale) * t;
        var tileSize = p.BaseTileSize * scale;

        // x_center: 視野水平置中，依 relCol 與當前列的 tileSize 排列
        var xCenter = p.ViewWidth * 0.5f + relCol * tileSize;

        return new ProjectedTile(
            X: xCenter - tileSize * 0.5f,
            Y: yCenter - tileSize * 0.5f,
            Width: tileSize,
            Height: tileSize,
            Scale: scale,
            T: t);
    }

    /// <summary>判斷某 (relRow, relCol) 是否在 5×5 可見範圍。</summary>
    public static bool IsVisible(int relRow, int relCol, ProjectionParams p)
    {
        var halfRows = p.VisibleRows / 2; // 5 → 2
        var halfCols = p.VisibleCols / 2; // 5 → 2
        return relRow >= -halfRows && relRow <= halfRows
            && relCol >= -halfCols && relCol <= halfCols;
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

/// <summary>單一地塊投影結果。座標為 viewBox 內 top-left。</summary>
public readonly record struct ProjectedTile(
    float X,
    float Y,
    float Width,
    float Height,
    float Scale,
    float T);
