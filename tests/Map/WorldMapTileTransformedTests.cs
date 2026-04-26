// Phase 2 任務 11 Stage 4 — WorldMap.TileTransformed event 測試。
//
// 驗證：
// - NotifyTileTransformed 觸發 TileTransformed event，攜帶 (row, col, oldT, newT)
// - 在 BeginEventBatch 內 NotifyTileTransformed 延後到 flush；priority TileTransformed=70
//   排在 TileChanged=10 之後 → 紋理先換、再閃爍
// - 不在 batch 內直接 emit
// - 整合：呼 NotifyTileChanged + NotifyTileTransformed → flush 順序正確
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using FluentAssertions;

namespace CardNarrative.Tests.Map;

public class WorldMapTileTransformedTests
{
    private sealed class NoSubstituteRandom : IRandomProvider
    {
        public double NextDouble() => 0.99;
        public int Next(int maxExclusive) => 0;
    }

    [Fact]
    public void NotifyTileTransformed_OutsideBatch_FiresImmediately()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        var calls = new List<(int row, int col, MapTerrain oldT, MapTerrain newT)>();
        map.TileTransformed += (r, c, o, n) => calls.Add((r, c, o, n));

        map.NotifyTileTransformed(2, 3, MapTerrain.Forest, MapTerrain.Mountain);

        calls.Should().HaveCount(1);
        calls[0].Should().Be((2, 3, MapTerrain.Forest, MapTerrain.Mountain));
    }

    [Fact]
    public void NotifyTileTransformed_InsideBatch_DefersUntilEnd()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        var calls = 0;
        map.TileTransformed += (_, _, _, _) => calls++;

        map.BeginEventBatch();
        map.NotifyTileTransformed(0, 0, MapTerrain.Path, MapTerrain.Water);
        calls.Should().Be(0);

        map.EndEventBatch();
        calls.Should().Be(1);
    }

    [Fact]
    public void Batch_TileChangedFiresBeforeTileTransformed_TextureSwapBeforeFlash()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        var order = new List<string>();
        map.TileChanged += (_, _) => order.Add("TileChanged");
        map.TileTransformed += (_, _, _, _) => order.Add("TileTransformed");

        map.BeginEventBatch();
        // 故意先 enqueue TileTransformed
        map.NotifyTileTransformed(4, 4, MapTerrain.Building, MapTerrain.Mountain);
        map.NotifyTileChanged(4, 4);
        map.EndEventBatch();

        // priority TileChanged=10 < TileTransformed=70 → 紋理先換、再閃爍
        order.Should().ContainInOrder("TileChanged", "TileTransformed");
    }

    [Fact]
    public void NotifyTileTransformed_DistinctEventFromTileChanged()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        var changedCount = 0;
        var transformedCount = 0;
        map.TileChanged += (_, _) => changedCount++;
        map.TileTransformed += (_, _, _, _) => transformedCount++;

        // 只發 TileChanged 不發 TileTransformed
        map.NotifyTileChanged(0, 0);
        changedCount.Should().Be(1);
        transformedCount.Should().Be(0);

        // 只發 TileTransformed 不發 TileChanged
        map.NotifyTileTransformed(1, 1, MapTerrain.Forest, MapTerrain.Grass);
        changedCount.Should().Be(1);
        transformedCount.Should().Be(1);
    }
}
