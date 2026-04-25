using CardNarrative.Core.Cards;
using FluentAssertions;

namespace CardNarrative.Tests.Cards;

public class CompanionApServiceTests
{
    private sealed class SeqRandom : IRandomProvider
    {
        private readonly Queue<double> _seq;
        public SeqRandom(params double[] vals) { _seq = new Queue<double>(vals); }
        public double NextDouble() => _seq.Count > 0 ? _seq.Dequeue() : 0.5;
        public int Next(int maxExclusive) => 0;
    }

    [Fact]
    public void TrySubstitute_NoCompanions_ReturnsNull()
    {
        var svc = new CompanionApService(new SeqRandom());
        svc.TrySubstitute(new List<CompanionAiState>()).Should().BeNull();
    }

    [Fact]
    public void TrySubstitute_FirstCompanionRollsBelowChance_TakesAp()
    {
        var c1 = new CompanionAiState("c1", "C1", 2);
        var c2 = new CompanionAiState("c2", "C2", 2);
        var svc = new CompanionApService(new SeqRandom(0.3)); // < 0.5

        var taken = svc.TrySubstitute(new[] { c1, c2 });

        taken.Should().Be(c1);
        c1.RemainingAp.Should().Be(1);
        c2.RemainingAp.Should().Be(2);
    }

    [Fact]
    public void TrySubstitute_FirstFails_TriesSecond()
    {
        var c1 = new CompanionAiState("c1", "C1", 2);
        var c2 = new CompanionAiState("c2", "C2", 2);
        var svc = new CompanionApService(new SeqRandom(0.9, 0.1)); // 第 1 失敗、第 2 成功

        var taken = svc.TrySubstitute(new[] { c1, c2 });

        taken.Should().Be(c2);
        c1.RemainingAp.Should().Be(2);
        c2.RemainingAp.Should().Be(1);
    }

    [Fact]
    public void TrySubstitute_AllFail_ReturnsNull()
    {
        var c1 = new CompanionAiState("c1", "C1", 2);
        var svc = new CompanionApService(new SeqRandom(0.9, 0.9));

        svc.TrySubstitute(new[] { c1 }).Should().BeNull();
        c1.RemainingAp.Should().Be(2);
    }

    [Fact]
    public void TrySubstitute_SkipsCompanionWithZeroAp()
    {
        var c1 = new CompanionAiState("c1", "C1", 0); // 0 AP 直接跳過
        var c2 = new CompanionAiState("c2", "C2", 2);
        var svc = new CompanionApService(new SeqRandom(0.1)); // 第 1 個值給 c2 用

        var taken = svc.TrySubstitute(new[] { c1, c2 });

        taken.Should().Be(c2);
        c2.RemainingAp.Should().Be(1);
    }

    [Fact]
    public void TrySubstitute_DistributionAround50Percent_With1000Trials()
    {
        // 用真隨機驗證分布
        var random = new SystemRandomProvider(seed: 42);
        var svc = new CompanionApService(random);
        int hits = 0;
        const int trials = 1000;

        for (int i = 0; i < trials; i++)
        {
            var c = new CompanionAiState("c", "C", 1);
            if (svc.TrySubstitute(new[] { c }) != null) hits++;
        }

        // seed 42 + 1000 trials，命中率應在 [0.40, 0.60] 之間
        var rate = hits / (double)trials;
        rate.Should().BeInRange(0.40, 0.60);
    }
}
