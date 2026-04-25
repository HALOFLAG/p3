using CardNarrative.Core.Services;

namespace CardNarrative.Tests.Services;

internal sealed class FakeDiceService : IDiceService
{
    private readonly Queue<RollResult> _rolls;
    public FakeDiceService(params RollResult[] rolls) => _rolls = new Queue<RollResult>(rolls);
    public RollResult Roll2d6() => _rolls.Count > 0 ? _rolls.Dequeue() : new RollResult(3, 3);
    public int RollD(int sides) => 1;
}
