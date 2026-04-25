namespace CardNarrative.Core.Cards;

/// <summary>
/// 抽象 random provider，便於 DeckService 重洗與 CompanionApService 50% 骰機注入確定值測試。
/// </summary>
public interface IRandomProvider
{
    /// <summary>回傳 [0, 1) 浮點數。</summary>
    double NextDouble();

    /// <summary>回傳 [0, maxExclusive) 整數，用於 Fisher-Yates 洗牌。</summary>
    int Next(int maxExclusive);
}

/// <summary>包裝 System.Random 的標準實作。</summary>
public sealed class SystemRandomProvider : IRandomProvider
{
    private readonly Random _random;

    public SystemRandomProvider(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public double NextDouble() => _random.NextDouble();

    public int Next(int maxExclusive) => _random.Next(maxExclusive);
}
