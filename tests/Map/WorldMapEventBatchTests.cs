// Phase 2 任務 11 Stage 3.3 — WorldMap event batch (buffer) 測試。
// 驗證：
// - BeginEventBatch / EndEventBatch 嵌套深度計數正確
// - 沒 batch 時 Notify* 直接 emit
// - 在 batch 內 Notify* 暫存，EndEventBatch 到最外層才 flush
// - flush 時依 EventPriority 排序 emit（TileChanged < PlayerMoved < HpChanged 等）
// - 嵌套 batch 不提早 flush（內層 End 不 flush）
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using FluentAssertions;

namespace CardNarrative.Tests.Map;

public class WorldMapEventBatchTests
{
    private sealed class NoSubstituteRandom : IRandomProvider
    {
        public double NextDouble() => 0.99;
        public int Next(int maxExclusive) => 0;
    }

    [Fact]
    public void NotifyOutsideBatch_EmitsImmediately()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        var calls = new List<string>();
        map.TileChanged += (r, c) => calls.Add($"tile({r},{c})");

        map.NotifyTileChanged(2, 3);

        calls.Should().HaveCount(1);
        calls[0].Should().Be("tile(2,3)");
    }

    [Fact]
    public void NotifyInsideBatch_DefersUntilEndEventBatch()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        var calls = new List<string>();
        map.TileChanged += (r, c) => calls.Add($"tile({r},{c})");

        map.BeginEventBatch();
        map.NotifyTileChanged(2, 3);
        calls.Should().BeEmpty(); // 尚未 flush

        map.EndEventBatch();
        calls.Should().HaveCount(1); // flush 後才 emit
    }

    [Fact]
    public void Batch_FlushOrdersByEventPriority()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        var order = new List<string>();
        map.PlayerMoved += (_, _, _, _) => order.Add("PlayerMoved");
        map.HpChanged += _ => order.Add("HpChanged");
        map.ApChanged += (_, _) => order.Add("ApChanged");
        map.TileChanged += (_, _) => order.Add("TileChanged");

        map.BeginEventBatch();
        // 故意打亂 enqueue 順序
        map.NotifyApChanged(2, 3);          // priority 35
        map.NotifyTileChanged(0, 0);        // priority 10
        map.NotifyHpChanged(5);             // priority 30
        map.NotifyPlayerMoved(0, 0, 1, 1);  // priority 20
        map.EndEventBatch();

        // 依 priority asc：TileChanged → PlayerMoved → HpChanged → ApChanged
        order.Should().ContainInOrder("TileChanged", "PlayerMoved", "HpChanged", "ApChanged");
    }

    [Fact]
    public void NestedBatch_OnlyFlushesAtOutermostEnd()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        var calls = new List<string>();
        map.TileChanged += (r, c) => calls.Add($"tile({r},{c})");

        map.BeginEventBatch();           // depth=1
        map.NotifyTileChanged(0, 0);
        map.BeginEventBatch();           // depth=2
        map.NotifyTileChanged(1, 1);
        map.EndEventBatch();             // depth=1 — 不 flush
        calls.Should().BeEmpty();

        map.NotifyTileChanged(2, 2);
        map.EndEventBatch();             // depth=0 — flush all 3
        calls.Should().HaveCount(3);
    }

    [Fact]
    public void EndEventBatch_WithoutBegin_NoOp()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        // 不應 throw
        var act = () => map.EndEventBatch();
        act.Should().NotThrow();
    }

    [Fact]
    public void Batch_EmptyQueue_NoOp()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        var calls = 0;
        map.TileChanged += (_, _) => calls++;

        map.BeginEventBatch();
        map.EndEventBatch();

        calls.Should().Be(0);
    }
}
