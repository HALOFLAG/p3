// Phase 3 v1.12 Stage 2 — TileChoiceBatch 後端「3 張選 1」批次選擇驗證。
// 規格書 §1.5 / §3.1.4 — BeginMapExpand 填批次、SelectFromBatch 取一張、Cancel held 退末尾。
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapBatchTests
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
            gridSize: 11,
            startPosition: new Position(5, 5));
        var map = new WorldMap(state, module, new NoSubstituteRandom());
        return (module, state, map);
    }

    /// <summary>
    /// v1.12 Stage 6 後：abandoned-mansion 已含 prologue.tileBatches，CreateNew 會把 TileDeck 留空。
    /// 需要「無 tileBatches」既有 3 卡 deck 抽法的測試 → 呼叫此 helper 把 PendingTileBatches 攤平回 TileDeck。
    /// </summary>
    private static void DrainBatchesToDeck(GameState state)
    {
        foreach (var batch in state.PendingTileBatches)
            foreach (var id in batch)
                state.TileDeck.Add(id);
        state.PendingTileBatches.Clear();
    }

    [Fact]
    public void BeginMapExpand_EmptyBatch_DrawsUpToThreeFromDeck()
    {
        var (_, state, map) = NewStateBackedMap();
        DrainBatchesToDeck(state); // 排空 PendingTileBatches → 走 TileDeck fallback 路徑
        var deckCountBefore = state.TileDeck.Count;
        deckCountBefore.Should().BeGreaterThan(2);
        state.TileChoiceBatch.Should().BeEmpty();

        map.BeginMapExpand().Should().BeTrue();

        state.TileChoiceBatch.Count.Should().Be(3);
        state.TileDeck.Count.Should().Be(deckCountBefore - 3);
        state.CurrentPlayer.HeldTileId.Should().BeNull(); // 不再自動設 held
    }

    [Fact]
    public void BeginMapExpand_DeckHasOneTile_FillsBatchWithOne()
    {
        var (_, state, map) = NewStateBackedMap();
        DrainBatchesToDeck(state); // 排空 PendingTileBatches，避免 BeginMapExpand 走 batch 路徑
        // 把 TileDeck 收縮成只剩 1 張
        var only = state.TileDeck[0];
        state.TileDeck.Clear();
        state.TileDeck.Add(only);

        map.BeginMapExpand().Should().BeTrue();

        state.TileChoiceBatch.Count.Should().Be(1);
        state.TileChoiceBatch[0].Should().Be(only);
        state.TileDeck.Should().BeEmpty();
    }

    [Fact]
    public void BeginMapExpand_BatchNonEmpty_DoesNotDrawNew()
    {
        // 第一次 Begin 抽 3 張 → Cancel（不選）→ 批次保留
        // 第二次 Begin 應沿用既有批次，TileDeck 不再被消耗
        var (_, state, map) = NewStateBackedMap();
        map.BeginMapExpand();
        var deckAfterFirst = state.TileDeck.Count;
        var batchSnapshot = state.TileChoiceBatch.ToList();
        map.CancelMapExpand();

        map.BeginMapExpand().Should().BeTrue();

        state.TileChoiceBatch.Should().Equal(batchSnapshot); // 同一批
        state.TileDeck.Count.Should().Be(deckAfterFirst);    // 沒再抽
    }

    [Fact]
    public void SelectFromBatch_ValidIndex_SetsHeldAndRemoves()
    {
        var (_, state, map) = NewStateBackedMap();
        map.BeginMapExpand();
        var pickId = state.TileChoiceBatch[1];
        var sizeBefore = state.TileChoiceBatch.Count;

        map.SelectFromBatch(1).Should().BeTrue();

        state.CurrentPlayer.HeldTileId.Should().Be(pickId);
        state.TileChoiceBatch.Count.Should().Be(sizeBefore - 1);
        state.TileChoiceBatch.Should().NotContain(pickId);
    }

    [Fact]
    public void SelectFromBatch_ReSelect_SwapsHeldAndBatch()
    {
        // v1.12 Stage 5：SelectFromBatch 改吃「視覺 slot idx」(0-2)；
        // 虛擬 slot 投影 — 持有時 slot[origIdx]=held，其他 slot 對應 batch（右移跳過 held 占的格）。
        var (_, state, map) = NewStateBackedMap();
        DrainBatchesToDeck(state); // 走 deck fallback 確保有 3 張可抽
        map.BeginMapExpand();
        var firstPick = state.TileChoiceBatch[0];
        var secondPick = state.TileChoiceBatch[1];

        // 先點視覺 slot 0 (firstPick) → held=firstPick，batch=[secondPick, third]，origIdx=0
        map.SelectFromBatch(0);
        state.CurrentPlayer.HeldTileId.Should().Be(firstPick);
        state.CurrentPlayer.HeldOriginalBatchIdx.Should().Be(0);

        // 虛擬 slot 1 顯示 batch[0] = secondPick；點視覺 slot 1 → re-select swap
        map.SelectFromBatch(1).Should().BeTrue();

        state.CurrentPlayer.HeldTileId.Should().Be(secondPick);
        state.CurrentPlayer.HeldOriginalBatchIdx.Should().Be(1);
        state.TileChoiceBatch[0].Should().Be(firstPick);
        state.TileChoiceBatch.Count.Should().Be(2);
    }

    [Fact]
    public void SelectFromBatch_SameSlotTwice_IsNoop()
    {
        // v1.12 Stage 5：點到 held 自己的視覺 slot → no-op（不改變狀態）
        var (_, state, map) = NewStateBackedMap();
        map.BeginMapExpand();
        map.SelectFromBatch(1);
        var heldBefore = state.CurrentPlayer.HeldTileId;
        var batchBefore = state.TileChoiceBatch.ToList();

        map.SelectFromBatch(1).Should().BeFalse();

        state.CurrentPlayer.HeldTileId.Should().Be(heldBefore);
        state.TileChoiceBatch.Should().Equal(batchBefore);
    }

    [Fact]
    public void CancelMapExpand_HeldReturnedToOriginalSlot()
    {
        // v1.12 Stage 5：Cancel 把 held 插回原 visual slot（不再退末尾）。
        var (_, state, map) = NewStateBackedMap();
        map.BeginMapExpand();
        map.SelectFromBatch(1); // origIdx=1
        var heldId = state.CurrentPlayer.HeldTileId!;
        var batchSizeBefore = state.TileChoiceBatch.Count;

        map.CancelMapExpand();

        state.CurrentPlayer.HeldTileId.Should().BeNull();
        state.CurrentPlayer.HeldOriginalBatchIdx.Should().BeNull();
        state.TileChoiceBatch.Count.Should().Be(batchSizeBefore + 1);
        state.TileChoiceBatch[1].Should().Be(heldId);
        map.Mode.Should().Be(InteractionMode.Idle);
    }
}
