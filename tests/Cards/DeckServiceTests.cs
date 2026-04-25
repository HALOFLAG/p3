using CardNarrative.Core.Cards;
using FluentAssertions;

namespace CardNarrative.Tests.Cards;

public class DeckServiceTests
{
    /// <summary>確定性 random，便於驗證洗牌行為。</summary>
    private sealed class FixedRandom : IRandomProvider
    {
        private readonly Queue<double> _doubles;
        private readonly Queue<int> _ints;
        public FixedRandom(IEnumerable<double> doubles, IEnumerable<int> ints)
        {
            _doubles = new Queue<double>(doubles);
            _ints = new Queue<int>(ints);
        }
        public double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 0.5;
        public int Next(int maxExclusive) => _ints.Count > 0 ? _ints.Dequeue() % maxExclusive : 0;
    }

    private static DeckService<string> NewDeck() => new(new FixedRandom(new[] { 0.5 }, new[] { 0, 0, 0, 0, 0 }));

    [Fact]
    public void LoadInitial_PopulatesDrawAndClearsOthers()
    {
        var deck = NewDeck();
        deck.LoadInitial(new[] { "a", "b", "c" });

        deck.DrawCount.Should().Be(3);
        deck.DiscardCount.Should().Be(0);
        deck.RemovedCount.Should().Be(0);
    }

    [Fact]
    public void DrawOne_FromNonEmptyDeck_ReducesDrawByOne()
    {
        var deck = NewDeck();
        deck.LoadInitial(new[] { "a", "b", "c" });

        var card = deck.DrawOne();

        card.Should().NotBeNull();
        deck.DrawCount.Should().Be(2);
    }

    [Fact]
    public void DrawOne_EmptyBothPiles_ReturnsNull()
    {
        var deck = NewDeck();
        deck.DrawOne().Should().BeNull();
    }

    [Fact]
    public void DrawOne_DrawEmptyButDiscardHasCards_ReshufflesAndDraws()
    {
        var deck = NewDeck();
        deck.LoadInitial(new[] { "a" });
        var first = deck.DrawOne();
        first.Should().NotBeNull();
        deck.DiscardCard(first!);
        deck.DiscardCard("b");
        deck.DrawCount.Should().Be(0);
        deck.DiscardCount.Should().Be(2);

        var next = deck.DrawOne();

        next.Should().NotBeNull();
        deck.DrawCount.Should().Be(1); // 重洗後抽 1 → 剩 1
        deck.DiscardCount.Should().Be(0);
    }

    [Fact]
    public void DrawMany_ReturnsRequestedCount_WhenEnoughAvailable()
    {
        var deck = NewDeck();
        deck.LoadInitial(new[] { "a", "b", "c", "d", "e" });

        var drawn = deck.DrawMany(3);

        drawn.Should().HaveCount(3);
        deck.DrawCount.Should().Be(2);
    }

    [Fact]
    public void DrawMany_ReturnsAvailableOnly_WhenInsufficient()
    {
        var deck = NewDeck();
        deck.LoadInitial(new[] { "a", "b" });

        var drawn = deck.DrawMany(5);

        drawn.Should().HaveCount(2);
        deck.DrawCount.Should().Be(0);
    }

    [Fact]
    public void DiscardCard_AddsToDiscardPile()
    {
        var deck = NewDeck();
        deck.DiscardCard("x");
        deck.DiscardCount.Should().Be(1);
    }

    [Fact]
    public void RemoveCard_AddsToRemovedPile_NotReshuffled()
    {
        var deck = NewDeck();
        deck.LoadInitial(new[] { "a" });
        var card = deck.DrawOne();
        deck.RemoveCard(card!);

        deck.RemovedCount.Should().Be(1);
        deck.DrawOne().Should().BeNull(); // 不會被洗回
    }

    [Fact]
    public void ReshuffleDiscardIntoDraw_MovesAllAndClearsDiscard()
    {
        var deck = NewDeck();
        deck.DiscardCard("a");
        deck.DiscardCard("b");

        deck.ReshuffleDiscardIntoDraw();

        deck.DrawCount.Should().Be(2);
        deck.DiscardCount.Should().Be(0);
    }
}
