using System.Linq;
using CardNarrative.Core.Map;
using FluentAssertions;

namespace CardNarrative.Tests.Map;

public class ProjectionTests
{
    private readonly ProjectionParams _p = Projection.Default;

    [Fact]
    public void Project_FarthestRow_BackEdgeAtVanishingPoint()
    {
        // 7×7 + 新 t 公式：最遠列 (relRow=-3) 的後緣應落在 vpY (= 視野頂端)
        var quad = Projection.ProjectQuad(relRow: -3, relCol: 0, _p);
        quad.BackLeft.Y.Should().BeApproximately(_p.VanishingPointY, 0.01f);
        quad.BackRight.Y.Should().BeApproximately(_p.VanishingPointY, 0.01f);
    }

    [Fact]
    public void Project_NearestRow_FrontEdgeAtGround()
    {
        // 最近列 (relRow=+3) 的前緣應落在 groundY (= 視野底端)，不會超出。
        var quad = Projection.ProjectQuad(relRow: 3, relCol: 0, _p);
        quad.FrontLeft.Y.Should().BeApproximately(_p.GroundY, 0.01f);
        quad.FrontRight.Y.Should().BeApproximately(_p.GroundY, 0.01f);
    }

    /// <summary>幾何尺度公式：scale(t) = FarScale × (1/FarScale)^t</summary>
    private float GeometricScale(float t) =>
        _p.FarScale * (float)System.Math.Pow(1.0 / _p.FarScale, t);

    [Fact]
    public void Project_PlayerCenter_TileCenteredOnViewport()
    {
        // relRow=0：t = 0.5
        var result = Projection.Project(relRow: 0, relCol: 0, _p);

        result.T.Should().BeApproximately(0.5f, 0.001f);

        var actualXCenter = result.X + result.Width * 0.5f;
        actualXCenter.Should().BeApproximately(_p.ViewWidth * 0.5f, 0.01f);

        // 幾何尺度：Y normalized = (scale(0.5) - FarScale) / (1 - FarScale)
        var scaleAtHalf = GeometricScale(0.5f);
        var normalizedY = (scaleAtHalf - _p.FarScale) / (1f - _p.FarScale);
        var expectedYCenter = _p.VanishingPointY + (_p.GroundY - _p.VanishingPointY) * normalizedY;
        var actualYCenter = result.Y + result.Height * 0.5f;
        actualYCenter.Should().BeApproximately(expectedYCenter, 0.05f);
    }

    [Fact]
    public void Project_FarthestRowScale_AtCenter()
    {
        // 中心 t = 0.0714，scale = FarScale × (1/FarScale)^0.0714 ≈ FarScale × 1.044
        var farTile = Projection.Project(relRow: -3, relCol: 0, _p);
        var expectedScale = GeometricScale(0.5f / _p.VisibleRows);
        farTile.Scale.Should().BeApproximately(expectedScale, 0.001f);
        farTile.Width.Should().BeApproximately(_p.BaseTileSize * expectedScale, 0.01f);
    }

    [Fact]
    public void Project_NearestRowScale_AtCenter()
    {
        var nearTile = Projection.Project(relRow: 3, relCol: 0, _p);
        var expectedScale = GeometricScale(1f - 0.5f / _p.VisibleRows);
        nearTile.Scale.Should().BeApproximately(expectedScale, 0.001f);
    }

    [Fact]
    public void Project_ColumnEdges_FormStraightLine()
    {
        // 幾何尺度的關鍵性質：同欄列在螢幕上的中心點呈直線（dxCenter/dyCenter 為常數）
        // 紅線示意的「前後直線」效果
        var rows = new[] { -3, -2, -1, 0, 1, 2, 3 };
        var entries = rows.Select(r => Projection.Project(r, relCol: -3, _p)).ToArray();
        var centers = entries.Select(e => (X: e.X + e.Width * 0.5f, Y: e.Y + e.Height * 0.5f)).ToArray();

        // 任意三點應共線：取頭尾兩點為基準線，檢查中間每點偏離度 < 容忍值
        var (x0, y0) = centers[0];
        var (xN, yN) = centers[^1];
        var dx = xN - x0;
        var dy = yN - y0;
        for (int i = 1; i < centers.Length - 1; i++)
        {
            var (xi, yi) = centers[i];
            // 點 (xi, yi) 到 (x0,y0)→(xN,yN) 直線的垂直距離 = |cross(diff, line) / |line||
            var cross = (xi - x0) * dy - (yi - y0) * dx;
            var lineLen = System.MathF.Sqrt(dx * dx + dy * dy);
            var perpendicularDist = System.MathF.Abs(cross / lineLen);
            perpendicularDist.Should().BeLessThan(0.5f,
                $"row {rows[i]} 中心 ({xi:F2},{yi:F2}) 應在連接 row {rows[0]}/{rows[^1]} 的直線上");
        }
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Project_MirrorColumns_SameYButOppositeXOffset(int relRow)
    {
        var leftTile = Projection.Project(relRow, relCol: -3, _p);
        var rightTile = Projection.Project(relRow, relCol: 3, _p);

        leftTile.Y.Should().Be(rightTile.Y);
        leftTile.Width.Should().Be(rightTile.Width);

        var leftCenter = leftTile.X + leftTile.Width * 0.5f;
        var rightCenter = rightTile.X + rightTile.Width * 0.5f;
        var leftDistance = _p.ViewWidth * 0.5f - leftCenter;
        var rightDistance = rightCenter - _p.ViewWidth * 0.5f;
        leftDistance.Should().BeApproximately(rightDistance, 0.01f);
    }

    [Fact]
    public void Project_IntegratedScaleY_DeltasIncreaseWithDepth()
    {
        // 整合尺度 Y：遠列 Δy 小、近列 Δy 大（與該列寬度同步），但變化線性而非二次
        var rows = new[] { -3, -2, -1, 0, 1, 2, 3 };
        var entries = rows.Select(r => Projection.Project(r, 0, _p)).ToArray();
        var centers = entries.Select(e => e.Y + e.Height * 0.5f).ToArray();

        // Δy 嚴格遞增（從遠到近）
        for (int i = 2; i < centers.Length; i++)
        {
            var prevDelta = centers[i - 1] - centers[i - 2];
            var thisDelta = centers[i] - centers[i - 1];
            thisDelta.Should().BeGreaterThan(prevDelta,
                $"row {rows[i - 1]}→{rows[i]} (Δ={thisDelta:F2}) 應 > row {rows[i - 2]}→{rows[i - 1]} (Δ={prevDelta:F2})");
        }
    }

    [Fact]
    public void IsVisible_WithinSevenBySevenRange_ReturnsTrue()
    {
        for (int r = -3; r <= 3; r++)
        for (int c = -3; c <= 3; c++)
        {
            Projection.IsVisible(r, c, _p).Should().BeTrue($"({r},{c}) should be visible");
        }
    }

    [Theory]
    [InlineData(-4, 0)]
    [InlineData(4, 0)]
    [InlineData(0, -4)]
    [InlineData(0, 4)]
    public void IsVisible_OutsideSevenBySeven_ReturnsFalse(int relRow, int relCol)
    {
        Projection.IsVisible(relRow, relCol, _p).Should().BeFalse();
    }

    // === ProjectQuad（地面梯形）===

    [Fact]
    public void ProjectQuad_BackEdgeNarrowerThanFront()
    {
        var quad = Projection.ProjectQuad(relRow: 0, relCol: 0, _p);
        var backWidth = quad.BackRight.X - quad.BackLeft.X;
        var frontWidth = quad.FrontRight.X - quad.FrontLeft.X;
        backWidth.Should().BeLessThan(frontWidth);
        backWidth.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ProjectQuad_BackEdgeY_LessThanFrontEdgeY()
    {
        var quad = Projection.ProjectQuad(relRow: 0, relCol: 0, _p);
        quad.BackLeft.Y.Should().BeLessThan(quad.FrontLeft.Y);
        quad.BackRight.Y.Should().BeLessThan(quad.FrontRight.Y);
        quad.BackLeft.Y.Should().Be(quad.BackRight.Y);
        quad.FrontLeft.Y.Should().Be(quad.FrontRight.Y);
    }

    [Fact]
    public void ProjectQuad_PlayerTile_CenterAtViewportMidline()
    {
        var quad = Projection.ProjectQuad(relRow: 0, relCol: 0, _p);
        quad.Center.X.Should().BeApproximately(_p.ViewWidth * 0.5f, 0.01f);
    }

    [Fact]
    public void ProjectQuad_AllTilesFitWithinViewport()
    {
        // 新 t 公式保證所有列前後緣都在 [vpY, groundY] 範圍內，不超出畫面
        for (int r = -3; r <= 3; r++)
        {
            var quad = Projection.ProjectQuad(r, 0, _p);
            quad.BackLeft.Y.Should().BeGreaterThanOrEqualTo(_p.VanishingPointY - 0.01f,
                $"row {r} back 不應超出 vpY 之上");
            quad.FrontLeft.Y.Should().BeLessThanOrEqualTo(_p.GroundY + 0.01f,
                $"row {r} front 不應超出 groundY 之下");
        }
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ProjectQuad_AllRows_OneToOneAspectRatio(int relRow)
    {
        // 整合尺度 Y → 每列 Δy ∝ scale，與該列寬度同步 → 所有列都呈 ~1:1（不只 player row）
        var quad = Projection.ProjectQuad(relRow, 0, _p);
        var width = (quad.BackRight.X - quad.BackLeft.X + quad.FrontRight.X - quad.FrontLeft.X) * 0.5f;
        var height = quad.FrontLeft.Y - quad.BackLeft.Y;
        var ratio = width / height;
        ratio.Should().BeInRange(0.95f, 1.05f,
            $"row {relRow}: width={width:F2}, height={height:F2}, ratio={ratio:F3}");
    }

    [Fact]
    public void ProjectQuad_FarRowNarrowerThanNearRow()
    {
        // 強透視確認：最遠列的寬度應顯著小於最近列（~ FarScale 的比例）
        var farQuad = Projection.ProjectQuad(relRow: -3, relCol: 0, _p);
        var nearQuad = Projection.ProjectQuad(relRow: 3, relCol: 0, _p);

        var farWidth = farQuad.BackRight.X - farQuad.BackLeft.X;  // 最遠後緣（最窄處）
        var nearWidth = nearQuad.FrontRight.X - nearQuad.FrontLeft.X; // 最近前緣（最寬處）

        var ratio = farWidth / nearWidth;
        ratio.Should().BeApproximately(_p.FarScale, 0.05f,
            $"最遠後緣寬 / 最近前緣寬 應接近 FarScale={_p.FarScale}, 實際={ratio:F3}");
    }
}
