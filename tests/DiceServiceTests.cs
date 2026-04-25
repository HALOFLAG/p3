using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests;

public class DiceServiceTests
{
    [Fact]
    public void Roll2d6_WithSameSeed_ProducesIdenticalSequence()
    {
        var a = new SeededDiceService(1234);
        var b = new SeededDiceService(1234);

        var rollsA = Enumerable.Range(0, 50).Select(_ => a.Roll2d6()).ToList();
        var rollsB = Enumerable.Range(0, 50).Select(_ => b.Roll2d6()).ToList();

        rollsA.Should().Equal(rollsB);
    }

    [Fact]
    public void Roll2d6_WithDifferentSeeds_ProducesDifferentSequence()
    {
        var a = new SeededDiceService(1);
        var b = new SeededDiceService(2);

        var rollsA = Enumerable.Range(0, 50).Select(_ => a.Roll2d6()).ToList();
        var rollsB = Enumerable.Range(0, 50).Select(_ => b.Roll2d6()).ToList();

        rollsA.Should().NotEqual(rollsB);
    }

    [Fact]
    public void Roll2d6_ValuesAlwaysBetween1And6()
    {
        var dice = new SeededDiceService(42);
        for (int i = 0; i < 10_000; i++)
        {
            var roll = dice.Roll2d6();
            roll.D1.Should().BeInRange(1, 6);
            roll.D2.Should().BeInRange(1, 6);
            roll.Total.Should().BeInRange(2, 12);
        }
    }

    [Fact]
    public void Roll2d6_DetectsDouble6AndDouble1()
    {
        var dice = new SeededDiceService(42);
        bool sawDouble6 = false, sawDouble1 = false;
        for (int i = 0; i < 100_000; i++)
        {
            var roll = dice.Roll2d6();
            if (roll.IsDouble6) sawDouble6 = true;
            if (roll.IsDouble1) sawDouble1 = true;
            if (sawDouble6 && sawDouble1) break;
        }
        sawDouble6.Should().BeTrue("with 100k rolls we should see at least one 6-6");
        sawDouble1.Should().BeTrue("with 100k rolls we should see at least one 1-1");
    }

    [Fact]
    public void RollD_RespectsBounds()
    {
        var dice = new SeededDiceService(7);
        for (int i = 0; i < 1000; i++)
            dice.RollD(20).Should().BeInRange(1, 20);
    }

    [Fact]
    public void RollD_ZeroSides_Throws()
    {
        var dice = new SeededDiceService(7);
        var act = () => dice.RollD(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
