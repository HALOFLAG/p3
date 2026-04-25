// TurnLogService — 記錄玩家可見的回合日誌（最多 50 筆環形緩衝）。
// 由 TurnLoop / BattleEngine / EventResolver 透過 ITurnLogSink.Append 主動推送，
// UI (MainViewModel) 綁 Entries + EntryAppended 事件顯示成捲動列表。
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public sealed class TurnLogService : ITurnLogSink
{
    public const int Capacity = 50;

    private readonly Func<(int BigRound, int SubTurn)> _turnProbe;
    private readonly LinkedList<TurnLogEntry> _entries = new();

    public TurnLogService(GameState? state = null)
        : this(state is null
            ? () => (0, 0)
            : () => (state.CurrentBigRound, state.CurrentPlayerIndex))
    {
    }

    public TurnLogService(Func<(int BigRound, int SubTurn)> turnProbe)
    {
        _turnProbe = turnProbe;
    }

    public IReadOnlyCollection<TurnLogEntry> Entries => _entries;

    public event EventHandler<TurnLogEntry>? EntryAppended;

    public void Append(string text, TurnLogKind kind = TurnLogKind.Neutral)
    {
        var (round, sub) = _turnProbe();
        var entry = new TurnLogEntry(text, kind, round, sub);
        _entries.AddLast(entry);
        while (_entries.Count > Capacity)
            _entries.RemoveFirst();
        EntryAppended?.Invoke(this, entry);
    }

    public void Clear()
    {
        _entries.Clear();
    }
}
