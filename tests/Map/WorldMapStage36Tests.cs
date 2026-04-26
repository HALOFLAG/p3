// Phase 2 任務 11 Stage 3.6 — 兩個 bug fix 的覆蓋測試。
//
// Bug 1：state-mode DrawToHandLimit 抽牌堆空時無重洗 → 棄牌堆累積但抽不出。
//        修：規格書 §3.1.3 / §3.4.2 — sDeck.Count==0 且 sDiscard.Count>0 時自動 ReshuffleDiscardIntoDeck。
//
// Bug 2：state-mode 起始格顯示 card-back（IsExplored=false）。
//        修：IsExplored 門檻從 >= Familiar 改 >= Unfamiliar，
//        對應 Phase 1+2 standalone 起始格 IsExplored=true 行為。
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapStage36Tests
{
    private sealed class NoSubstituteRandom : IRandomProvider
    {
        public double NextDouble() => 0.99;
        public int Next(int maxExclusive) => 0;
    }

    private static (Module module, GameState state, WorldMap map) NewStateBackedMap()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var module = ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;
        var heroId = module.Characters.Keys.First();
        var state = GameState.CreateNew(
            module,
            chosenCharacterIds: new[] { heroId },
            chosenCompanionIds: module.Prologue.StartingCompanionIds,
            seed: 1234,
            gridSize: 9,
            startPosition: new Position(4, 4));
        var map = new WorldMap(state, module, new NoSubstituteRandom());
        return (module, state, map);
    }

    // === Bug 1：棄牌堆重洗 ===

    [Fact]
    public void DrawToHandLimit_StateMode_DeckEmpty_ReshufflesDiscardIntoDeck()
    {
        var (module, state, map) = NewStateBackedMap();
        var cards = module.ActionCards.Values.Take(8).ToList();
        map.LoadActionDeck(cards);

        // 載入後：5 張在 hand、3 張在 deck、0 在 discard
        state.CurrentPlayer.Hand.Count.Should().Be(5);
        state.CurrentPlayer.Deck.Count.Should().Be(3);
        state.CurrentPlayer.Discard.Count.Should().Be(0);

        // 模擬出 5 張卡 → 全進 discard
        var handBefore = state.CurrentPlayer.Hand.ToList();
        foreach (var cardId in handBefore)
        {
            // 直接寫入避免 AP 不足；驗證重洗本身
            state.CurrentPlayer.Hand.Remove(cardId);
            state.CurrentPlayer.Discard.Add(cardId);
        }
        state.CurrentPlayer.Hand.Count.Should().Be(0);
        state.CurrentPlayer.Discard.Count.Should().Be(5);
        state.CurrentPlayer.Deck.Count.Should().Be(3); // 仍剩 3 張

        // 抽到 hand limit：先消耗 deck 3 張，然後 deck 空 → 重洗 discard 5 張回 → 再抽 2
        // 結束時 hand=5、deck=0+5-2=3、discard=0
        // 實作從 sHand.Count < HandSizeMax 條件抽 5 張：deck 給 3 張 → deck 空 → 重洗 discard(5) → 再抽 2 張
        // hand=5, deck=3 (重洗後 5 張剩 3), discard=0
        // 用 reflection 不太好；改用 AdvanceTurn 觸發 DrawToHandLimit
        state.Phase = TurnPhase.Action; // 讓 AdvanceTurn 從 Idle/Action 過去；實際 WorldMap.AdvanceTurn 不檢查 GameState.Phase

        // WorldMap.AdvanceTurn 會檢查 Mode==Idle 與 Turn<TurnLimit
        map.AdvanceTurn().Should().BeTrue();

        state.CurrentPlayer.Hand.Count.Should().Be(5);
        state.CurrentPlayer.Discard.Count.Should().Be(0); // 全洗回了
        // deck 剩 (3 + 5) - 5 = 3 張
        state.CurrentPlayer.Deck.Count.Should().Be(3);
    }

    [Fact]
    public void DrawToHandLimit_StateMode_DeckAndDiscardBothEmpty_StopsAtZero()
    {
        var (_, state, map) = NewStateBackedMap();
        // 不 LoadActionDeck → state.Hand/Deck/Discard 都空（CreateNew 已抽完 character.StartingDeck？實際看）
        state.CurrentPlayer.Hand.Clear();
        state.CurrentPlayer.Deck.Clear();
        state.CurrentPlayer.Discard.Clear();

        // AdvanceTurn 觸發 DrawToHandLimit；無卡可抽應該安全 break、不 infinite loop
        var act = () => map.AdvanceTurn();
        act.Should().NotThrow();

        state.CurrentPlayer.Hand.Should().BeEmpty();
        state.CurrentPlayer.Deck.Should().BeEmpty();
        state.CurrentPlayer.Discard.Should().BeEmpty();
    }

    [Fact]
    public void DrawToHandLimit_StateMode_ReshuffleIsDeterministic()
    {
        // 同 seed + 同大回合 → 重洗結果應一致（deterministic）
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var module = ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;

        List<string> RunOnce()
        {
            var heroId = module.Characters.Keys.First();
            var state = GameState.CreateNew(
                module, new[] { heroId },
                module.Prologue.StartingCompanionIds, seed: 42,
                gridSize: 9, startPosition: new Position(4, 4));
            var map = new WorldMap(state, module, new NoSubstituteRandom());
            // 8 張卡進 deck → 抽 5 → 把 5 張 hand 移到 discard → 再抽 5（觸發重洗）
            map.LoadActionDeck(module.ActionCards.Values.Take(8).ToList());
            foreach (var c in state.CurrentPlayer.Hand.ToList())
            {
                state.CurrentPlayer.Hand.Remove(c);
                state.CurrentPlayer.Discard.Add(c);
            }
            map.AdvanceTurn();
            return state.CurrentPlayer.Hand.ToList();
        }

        var hand1 = RunOnce();
        var hand2 = RunOnce();
        hand1.Should().Equal(hand2, "same seed should produce same shuffle order");
    }

    // === Bug 2：起始格 IsExplored ===

    [Fact]
    public void StateMode_StartingTile_IsExploredTrue_AvoidsCardBackDisplay()
    {
        var (_, state, map) = NewStateBackedMap();
        // CreateNew 預設起始格 Level=Unfamiliar
        state.TileMap[(4, 4)].Level.Should().Be(ExplorationLevel.Unfamiliar);
        // Stage 3.6：IsExplored 門檻 >= Unfamiliar → true
        map.GetTile(4, 4).IsExplored.Should().BeTrue();
    }

    [Theory]
    [InlineData(ExplorationLevel.Unknown, false)]
    [InlineData(ExplorationLevel.Unfamiliar, true)]
    [InlineData(ExplorationLevel.Neutral, true)]
    [InlineData(ExplorationLevel.Familiar, true)]
    [InlineData(ExplorationLevel.Mastered, true)]
    public void StateMode_GetTile_IsExploredThreshold_GreaterEqualUnfamiliar(
        ExplorationLevel level, bool expected)
    {
        var (_, state, map) = NewStateBackedMap();
        state.TileMap[(4, 4)].Level = level;
        map.GetTile(4, 4).IsExplored.Should().Be(expected);
    }

    [Fact]
    public void StateMode_NewlyPlacedTile_LevelUnknown_DisplaysAsUnexplored()
    {
        // 模擬 MapExpand 剛放下的 tile：Level=Unknown → IsExplored=false（card-back）
        var (_, state, map) = NewStateBackedMap();
        state.TileMap[(0, 1)] = new PlacedTile { TileId = "forest-path", Level = ExplorationLevel.Unknown };
        var tile = map.GetTile(1, 0); // row=Y=1, col=X=0
        tile.IsPlaced.Should().BeTrue();
        tile.IsExplored.Should().BeFalse();
        // 玩家踏入 → Level 升到 Unfamiliar 或更高 → IsExplored=true
        state.TileMap[(0, 1)].Level = ExplorationLevel.Unfamiliar;
        map.GetTile(1, 0).IsExplored.Should().BeTrue();
    }
}
