// Phase 2 任務 11 Stage 3.1+3.2 — WorldMap dual-mode mutation dispatch 測試。
// 驗證 state-mode 下：
// - LoadActionDeck → state.CurrentPlayer.Deck/Hand 寫入；自動 Draw
// - DrawToHandLimit → 從 state.Deck 抽進 state.Hand
// - TryPlayCard → state.Hand.Remove + state.Discard.Add
// - BeginMapExpand → 從 state.TileDeck 抽，HeldTile 派生自 _heldTileId
// - TryPlaceHeldTile → state.TileMap[(col,row)] 新 PlacedTile
// - CancelMapExpand → 把 heldTileId 放回 state.TileDeck 最前
// - TryMovePlayerTo → 目標格 Level 升 Familiar（IsExplored 派生）
// - IsLegalPlacement / IsLegalMoveTarget / HasPlacedNeighbor 用 GetTile dispatch
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapDualModeMutationTests
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

    // === Hand / Deck dispatch ===

    [Fact]
    public void StateMode_LoadActionDeck_PopulatesStateDeckAndDrawsHand()
    {
        var (module, state, map) = NewStateBackedMap();
        var cards = module.ActionCards.Values.Take(8).ToList();

        map.LoadActionDeck(cards);

        // 8 張 ≤ HandSize 上限 5 → 抽 5 進 Hand，剩 3 在 Deck
        state.CurrentPlayer.Hand.Should().HaveCount(WorldMap.HandSizeMax);
        state.CurrentPlayer.Deck.Should().HaveCount(8 - WorldMap.HandSizeMax);
        state.CurrentPlayer.Discard.Should().BeEmpty();
        map.HandSize.Should().Be(WorldMap.HandSizeMax);
        map.ActionDeckRemaining.Should().Be(8 - WorldMap.HandSizeMax);
    }

    [Fact]
    public void StateMode_TryPlayCard_RemovesFromHandAddsToDiscard()
    {
        var (module, state, map) = NewStateBackedMap();
        var cards = module.ActionCards.Values.Take(8).ToList();
        map.LoadActionDeck(cards);

        var firstCardId = state.CurrentPlayer.Hand[0];
        var card = module.ActionCards[firstCardId];
        // 確保 AP 夠
        state.CurrentPlayer.ActionPoints = WorldMap.ApMax;

        var result = map.TryPlayCard(firstCardId);

        result.Success.Should().BeTrue();
        state.CurrentPlayer.Hand.Should().NotContain(firstCardId);
        state.CurrentPlayer.Discard.Should().Contain(firstCardId);
        map.ActionDiscardCount.Should().Be(1);
    }

    [Fact]
    public void StateMode_TryPlayCard_NotInHand_ReturnsFalse()
    {
        var (module, _, map) = NewStateBackedMap();
        map.LoadActionDeck(module.ActionCards.Values.Take(3).ToList());

        var result = map.TryPlayCard("nonexistent-card-id");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("不在手牌");
    }

    // === Tile / HeldTile dispatch ===

    [Fact]
    public void StateMode_BeginMapExpand_DrawsFromStateTileDeck()
    {
        var (module, state, map) = NewStateBackedMap();
        var initialDeckCount = state.TileDeck.Count;
        var topId = state.TileDeck[0];
        initialDeckCount.Should().BeGreaterThan(0);

        map.BeginMapExpand().Should().BeTrue();

        map.Mode.Should().Be(InteractionMode.MapExpand);
        map.HeldTile.Should().NotBeNull();
        // HeldTile 應反映該 tile 的 visualProfile.terrain
        var topTile = module.Tiles[topId];
        map.HeldTile.Should().Be(TileVisualProfileResolver.ResolveTerrain(topTile));
        state.TileDeck.Count.Should().Be(initialDeckCount - 1);
    }

    [Fact]
    public void StateMode_TryPlaceHeldTile_WritesToStateTileMap()
    {
        var (module, state, map) = NewStateBackedMap();
        var topId = state.TileDeck[0];
        map.BeginMapExpand();

        // 起點 (4,4) 的鄰居 (3,4) — 但 visualProfile 未必通過 tag rule
        // 這裡只測 state.TileMap 寫入，不挑 tile id；用 (4,5) 直接放
        // IsLegalPlacement 內 HasPlacedNeighbor 用 GetTile dispatch，state-mode 看到 (4,4) 已放
        map.TryPlaceHeldTile(4, 5).Should().BeTrue();

        state.TileMap.Should().ContainKey((5, 4)); // (col=5, row=4)
        state.TileMap[(5, 4)].TileId.Should().Be(topId);
        state.TileMap[(5, 4)].Level.Should().Be(ExplorationLevel.Unknown);
        map.Mode.Should().Be(InteractionMode.Idle);
        map.HeldTile.Should().BeNull();
    }

    [Fact]
    public void StateMode_CancelMapExpand_ReturnsHeldTileToStateDeck()
    {
        var (_, state, map) = NewStateBackedMap();
        var initialDeckCount = state.TileDeck.Count;
        var topId = state.TileDeck[0];
        map.BeginMapExpand();
        state.TileDeck.Count.Should().Be(initialDeckCount - 1);

        map.CancelMapExpand();

        state.TileDeck.Count.Should().Be(initialDeckCount);
        state.TileDeck[0].Should().Be(topId);
        map.Mode.Should().Be(InteractionMode.Idle);
        map.HeldTile.Should().BeNull();
    }

    [Fact]
    public void StateMode_TryMovePlayerTo_UpgradesTileLevelToFamiliar()
    {
        var (module, state, map) = NewStateBackedMap();
        // 在 (3,4) 加一格供移動目標
        var anyTileId = module.Tiles.Keys.First();
        state.TileMap[(4, 3)] = new PlacedTile { TileId = anyTileId, Level = ExplorationLevel.Unfamiliar };

        // BeginMoveMode → TryMovePlayerTo (3,4) — Row=3 Col=4 → state(X=4, Y=3)
        map.BeginMoveMode();
        var result = map.TryMovePlayerTo(3, 4);

        result.Should().Be(MovePlayerResult.Ok);
        state.CurrentPlayer.Position.Should().Be(new Position(4, 3));
        state.TileMap[(4, 3)].Level.Should().Be(ExplorationLevel.Familiar);
    }

    [Fact]
    public void StateMode_IsLegalPlacement_UsesGetTileDispatch()
    {
        var (_, state, map) = NewStateBackedMap();
        // 起點 (4,4) 已放 — 鄰居 (3,4)/(4,3)/(5,4)/(4,5) 應 legal
        map.IsLegalPlacement(3, 4).Should().BeTrue();
        map.IsLegalPlacement(4, 5).Should().BeTrue();
        // (4,4) 已放 — 不能再放
        map.IsLegalPlacement(4, 4).Should().BeFalse();
        // (0,0) 不是鄰居 → 不能放
        map.IsLegalPlacement(0, 0).Should().BeFalse();
    }

    // === RemainingTiles / NextTilePreview dispatch ===

    [Fact]
    public void StateMode_RemainingTiles_ReadsStateTileDeckCount()
    {
        var (_, state, map) = NewStateBackedMap();
        map.RemainingTiles.Should().Be(state.TileDeck.Count);

        state.TileDeck.Add("forest-path");
        map.RemainingTiles.Should().Be(state.TileDeck.Count);
    }

    [Fact]
    public void StateMode_NextTilePreview_ResolvesFromStateTileDeck()
    {
        var (module, state, map) = NewStateBackedMap();
        var preview = map.NextTilePreview;
        preview.Should().HaveCountLessThanOrEqualTo(3);
        preview.Should().HaveCountGreaterThanOrEqualTo(1);
        // 第一張應對應 state.TileDeck[0] 的 visualProfile.terrain
        var firstId = state.TileDeck[0];
        var firstTile = module.Tiles[firstId];
        preview[0].Should().Be(TileVisualProfileResolver.ResolveTerrain(firstTile));
    }
}
