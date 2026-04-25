namespace CardNarrative.Core.Cards;

/// <summary>
/// 同伴 AP 代消耗服務（規格書 §3.7 / §3.1.4）。
/// 玩家每消耗 1 AP 時，依隊伍順序逐位骰 50%；命中 → 同伴扣 AP，主角不扣。
/// 全未命中或無同伴回 false → 主角自己扣。
/// </summary>
public sealed class CompanionApService
{
    private readonly IRandomProvider _random;

    /// <summary>50% 機率（規格書 §3.1.4）。</summary>
    public const double SubstituteChance = 0.5;

    public CompanionApService(IRandomProvider random)
    {
        _random = random;
    }

    /// <summary>
    /// 嘗試讓某位同伴代消耗 1 AP。
    /// 依輸入順序（隊伍順序）逐位骰 50%；同伴 AP=0 跳過。
    /// </summary>
    /// <returns>命中時回傳代消耗的同伴；無人代則回 null。</returns>
    public CompanionAiState? TrySubstitute(IList<CompanionAiState> companions)
    {
        foreach (var c in companions)
        {
            if (c.RemainingAp <= 0) continue;
            if (_random.NextDouble() < SubstituteChance)
            {
                c.RemainingAp--;
                return c;
            }
        }
        return null;
    }
}
