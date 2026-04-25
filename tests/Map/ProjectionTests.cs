using CardNarrative.Core.Map;
using FluentAssertions;

namespace CardNarrative.Tests.Map;

public class ProjectionTests
{
    private readonly ProjectionParams _p = Projection.Default;

    [Fact]
    public void Project_FarthestRow_YNearVanishingPoint()
    {
        // relRow = -2 → depthIndex 0 → t=0 → y_center = vpY = 130
        var result = Projection.Project(relRow: -2, relCol: 0, _p);

        result.T.Should().Be(0f);
        // y is top-left of tile, so y_center = vpY → top-left = vpY - tileSize/2
        var expectedYCenter = _p.VanishingPointY;
        var actualYCenter = result.Y + result.Height * 0.5f;
        actualYCenter.Should().BeApproximately(expectedYCenter, 0.01f);
    }

    [Fact]
    public void Project_NearestRow_YNearGround()
    {
        // relRow = +2 → depthIndex 4 → t=1 → y_center = groundY = 400
        var result = Projection.Project(relRow: 2, relCol: 0, _p);

        result.T.Should().Be(1f);
        var expectedYCenter = _p.GroundY;
        var actualYCenter = result.Y + result.Height * 0.5f;
        actualYCenter.Should().BeApproximately(expectedYCenter, 0.01f);
    }

    [Fact]
    public void Project_PlayerCenter_TileCenteredOnViewport()
    {
        // relRow=0, relCol=0, depthIndex 2, t=0.5 → y = vpY + (groundY-vpY)*0.25
        var result = Projection.Project(relRow: 0, relCol: 0, _p);

        result.T.Should().Be(0.5f);

        var actualXCenter = result.X + result.Width * 0.5f;
        actualXCenter.Should().BeApproximately(_p.ViewWidth * 0.5f, 0.01f);

        // y_center = 130 + (400-130) * 0.25 = 130 + 67.5 = 197.5
        var actualYCenter = result.Y + result.Height * 0.5f;
        actualYCenter.Should().BeApproximately(197.5f, 0.01f);
    }

    [Fact]
    public void Project_FarRowTileSize_IsThirtyPercentOfBase()
    {
        // 規格書 §5.1.1：「漸進縮小至 ~30% 起始大小」
        var farTile = Projection.Project(relRow: -2, relCol: 0, _p);
        farTile.Scale.Should().Be(_p.FarScale);
        farTile.Width.Should().Be(_p.BaseTileSize * _p.FarScale);
    }

    [Fact]
    public void Project_NearRowTileSize_IsBaseSize()
    {
        var nearTile = Projection.Project(relRow: 2, relCol: 0, _p);
        nearTile.Scale.Should().Be(1f);
        nearTile.Width.Should().Be(_p.BaseTileSize);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Project_MirrorColumns_SameYButOppositeXOffset(int relRow)
    {
        // 同一列左右兩格應該有相同 y 與相反方向的 x 偏移
        var leftTile = Projection.Project(relRow, relCol: -2, _p);
        var rightTile = Projection.Project(relRow, relCol: 2, _p);

        leftTile.Y.Should().Be(rightTile.Y);
        leftTile.Width.Should().Be(rightTile.Width);

        var leftCenter = leftTile.X + leftTile.Width * 0.5f;
        var rightCenter = rightTile.X + rightTile.Width * 0.5f;
        var leftDistance = _p.ViewWidth * 0.5f - leftCenter;
        var rightDistance = rightCenter - _p.ViewWidth * 0.5f;
        leftDistance.Should().BeApproximately(rightDistance, 0.01f);
    }

    [Fact]
    public void Project_QuadraticDepthSpacing_FarRowsCompressedMoreThanNear()
    {
        // 規格書 §5.1.2：y = vpY + (groundY-vpY) * t² → 遠端壓縮、近端稀疏
        var t0 = Projection.Project(-2, 0, _p);
        var t1 = Projection.Project(-1, 0, _p);
        var t2 = Projection.Project(0, 0, _p);
        var t3 = Projection.Project(1, 0, _p);
        var t4 = Projection.Project(2, 0, _p);

        var d01 = t1.Y - t0.Y;
        var d12 = t2.Y - t1.Y;
        var d23 = t3.Y - t2.Y;
        var d34 = t4.Y - t3.Y;

        d01.Should().BeLessThan(d12);
        d12.Should().BeLessThan(d23);
        d23.Should().BeLessThan(d34);
    }

    [Fact]
    public void IsVisible_WithinFiveByFiveRange_ReturnsTrue()
    {
        for (int r = -2; r <= 2; r++)
        for (int c = -2; c <= 2; c++)
        {
            Projection.IsVisible(r, c, _p).Should().BeTrue($"({r},{c}) should be visible");
        }
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(3, 0)]
    [InlineData(0, -3)]
    [InlineData(0, 3)]
    public void IsVisible_OutsideFiveByFive_ReturnsFalse(int relRow, int relCol)
    {
        Projection.IsVisible(relRow, relCol, _p).Should().BeFalse();
    }
}
