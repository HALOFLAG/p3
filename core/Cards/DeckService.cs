namespace CardNarrative.Core.Cards;

/// <summary>
/// 行動卡抽 / 棄 / 移除 / 重洗（規格書 §3.4.2）。
/// 泛型化以便將來重用於其他卡類別（事件卡、戰鬥卡等不在本 Stage 範圍）。
/// </summary>
public sealed class DeckService<TCard> where TCard : class
{
    private readonly IRandomProvider _random;
    private readonly List<TCard> _draw = new();
    private readonly List<TCard> _discard = new();
    private readonly List<TCard> _removed = new();

    public IReadOnlyList<TCard> Draw => _draw;
    public IReadOnlyList<TCard> Discard => _discard;
    public IReadOnlyList<TCard> Removed => _removed;

    public int DrawCount => _draw.Count;
    public int DiscardCount => _discard.Count;
    public int RemovedCount => _removed.Count;
    public int TotalAvailable => _draw.Count + _discard.Count;

    public DeckService(IRandomProvider random)
    {
        _random = random;
    }

    /// <summary>初始化抽牌堆。會清空既有狀態並洗牌。</summary>
    public void LoadInitial(IEnumerable<TCard> cards)
    {
        _draw.Clear();
        _discard.Clear();
        _removed.Clear();
        _draw.AddRange(cards);
        Shuffle(_draw);
    }

    /// <summary>
    /// 抽一張卡。抽牌堆空時自動把棄牌堆洗回。兩堆都空回 null。
    /// </summary>
    public TCard? DrawOne()
    {
        if (_draw.Count == 0)
        {
            if (_discard.Count == 0) return null;
            ReshuffleDiscardIntoDraw();
        }

        var top = _draw[^1];
        _draw.RemoveAt(_draw.Count - 1);
        return top;
    }

    /// <summary>連抽多張，遇 null 提前停（牌堆耗盡）。</summary>
    public IReadOnlyList<TCard> DrawMany(int count)
    {
        var result = new List<TCard>(count);
        for (int i = 0; i < count; i++)
        {
            var card = DrawOne();
            if (card is null) break;
            result.Add(card);
        }
        return result;
    }

    /// <summary>把卡放進棄牌堆。</summary>
    public void DiscardCard(TCard card) => _discard.Add(card);

    /// <summary>把卡移到移除堆（不會被洗回）。</summary>
    public void RemoveCard(TCard card) => _removed.Add(card);

    /// <summary>把棄牌堆洗回抽牌堆（規格書 §3.1.3）。</summary>
    public void ReshuffleDiscardIntoDraw()
    {
        _draw.AddRange(_discard);
        _discard.Clear();
        Shuffle(_draw);
    }

    /// <summary>Fisher-Yates 洗牌。</summary>
    private void Shuffle(List<TCard> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
