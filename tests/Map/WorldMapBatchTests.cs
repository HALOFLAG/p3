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
            gridSize: 9,
            startPosition: new Position(4, 4));
        var map = new WorldMap(state, module, new NoSubstituteRandom());
        return (module, state, map);
    }

    [Fact]
    public void BeginMapExpand_EmptyBatch_DrawsUpToThreeFromDeck()
    {
        var (_, state, map) = NewStateBackedMap();
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
        var (_, state, map) = NewStateBackedMap();
        map.BeginMapExpand();
        var firstPick = state.TileChoiceBatch[0];
        var secondPickIdx = 1;
        var secondPickId = state.TileChoiceBatch[secondPickIdx];

        map.SelectFromBatch(0); // 先選 idx=0 → held=firstPick，批次剩 2 張（原 idx=1 現在是 idx=0、原 idx=2 現在是 idx=1）
        state.CurrentPlayer.HeldTileId.Should().Be(firstPick);
        state.TileChoiceBatch[0].Should().Be(secondPickId);

        // re-select 改選原 secondPick（批次中現在的 idx=0）
        map.SelectFromBatch(0).Should().BeTrue();

        state.CurrentPlayer.HeldTileId.Should().Be(secondPickId);
        // firstPick 應換回到原批次的 idx=0 位置
        state.TileChoiceBatch[0].Should().Be(firstPick);
        state.TileChoiceBatch.Count.Should().Be(2); // 批次長度不變
    }

    [Fact]
    public void CancelMapExpand_HeldReturnedToBatchEnd()
    {
        var (_, state, map) = NewStateBackedMap();
        map.BeginMapExpand();
        map.SelectFromBatch(0);
        var heldId = state.CurrentPlayer.HeldTileId!;
        var batchSizeBefore = state.TileChoiceBatch.Count;

        map.CancelMapExpand();

        state.CurrentPlayer.HeldTileId.Should().BeNull();
        state.TileChoiceBatch.Count.Should().Be(batchSizeBefore + 1);
        state.TileChoiceBatch[^1].Should().Be(heldId); // 退到末尾
        map.Mode.Should().Be(InteractionMode.Idle);
    }
}
