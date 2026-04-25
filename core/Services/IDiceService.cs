// IDiceService / SeededDiceService — 2d6 與 Nd 骰子服務。
// RollResult 提供 Total / IsDouble6 / IsDouble1（特殊事件判定用）。
// 正式使用 SeededDiceService（以 seed 決定性）；測試可注入 FakeDiceService。
namespace CardNarrative.Core.Services;

public readonly record struct RollResult(int D1, int D2)
{
    public int Total => D1 + D2;
    public bool IsDouble6 => D1 == 6 && D2 == 6;
    public bool IsDouble1 => D1 == 1 && D2 == 1;
}

public interface IDiceService
{
    RollResult Roll2d6();
    int RollD(int sides);
}

public sealed class SeededDiceService : IDiceService
{
    private readonly Random _random;

    public SeededDiceService(int seed)
    {
        _random = new Random(seed);
    }

    public RollResult Roll2d6()
    {
        var d1 = _random.Next(1, 7);
        var d2 = _random.Next(1, 7);
        return new RollResult(d1, d2);
    }

    public int RollD(int sides)
    {
        if (sides < 1) throw new ArgumentOutOfRangeException(nameof(sides));
        return _random.Next(1, sides + 1);
    }
}
