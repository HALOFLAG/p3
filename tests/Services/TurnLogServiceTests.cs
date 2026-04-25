using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class TurnLogServiceTests
{
    [Fact]
    public void Append_StoresEntriesInOrder()
    {
        var log = new TurnLogService(() => (1, 0));
        log.Append("first", TurnLogKind.Neutral);
        log.Append("hit", TurnLogKind.Hit);
        log.Append("miss", TurnLogKind.Miss);

        log.Entries.Should().HaveCount(3);
        log.Entries.Select(e => e.Text).Should().ContainInOrder("first", "hit", "miss");
        log.Entries.Select(e => e.Kind).Should().ContainInOrder(TurnLogKind.Neutral, TurnLogKind.Hit, TurnLogKind.Miss);
    }

    [Fact]
    public void Append_DropsOldestOnceCapacityExceeded()
    {
        var log = new TurnLogService(() => (1, 0));
        for (int i = 0; i < TurnLogService.Capacity + 5; i++)
            log.Append($"entry-{i}");

        log.Entries.Should().HaveCount(TurnLogService.Capacity);
        log.Entries.First().Text.Should().Be("entry-5");
        log.Entries.Last().Text.Should().Be($"entry-{TurnLogService.Capacity + 4}");
    }

    [Fact]
    public void Append_RaisesEntryAppendedEvent()
    {
        var log = new TurnLogService(() => (3, 1));
        TurnLogEntry? received = null;
        log.EntryAppended += (_, entry) => received = entry;

        log.Append("ping", TurnLogKind.Hit);

        received.Should().NotBeNull();
        received!.Text.Should().Be("ping");
        received.Kind.Should().Be(TurnLogKind.Hit);
        received.BigRound.Should().Be(3);
        received.SubTurn.Should().Be(1);
    }

    [Fact]
    public void Append_StampsTurnCoordsFromProbe()
    {
        int bigRound = 1;
        int sub = 0;
        var log = new TurnLogService(() => (bigRound, sub));
        log.Append("a");
        bigRound = 2; sub = 1;
        log.Append("b");

        var entries = log.Entries.ToList();
        entries[0].BigRound.Should().Be(1);
        entries[0].SubTurn.Should().Be(0);
        entries[1].BigRound.Should().Be(2);
        entries[1].SubTurn.Should().Be(1);
    }

    [Fact]
    public void Clear_EmptiesEntries()
    {
        var log = new TurnLogService(() => (1, 0));
        log.Append("a");
        log.Append("b");
        log.Clear();
        log.Entries.Should().BeEmpty();
    }
}
