// PR-B · 玩家狀態欄位補齊測試（規格書 §3.4 / §3.7 / §3.1.4）。
// 驗證 state-mode 下：
//   - HeldTileId dual-mode dispatch（BeginMapExpand / TryPlaceHeldTile / CancelMapExpand）→ state.CurrentPlayer.HeldTileId
//   - CompanionHp dual-mode dispatch → state.Companions[0].Hp
//   - LoadCompanion 對齊 GameState SoT（不覆蓋既有同 id 的 Hp）
//   - FirstObserveUsedThisTurn dual-mode dispatch → state.CurrentPlayer.ObservesThisTurn
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapStateBackedFieldsTests
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

    // === P0-3 · HeldTileId dispatch ===

    [Fact]
    public void StateMode_BeginMapExpand_WritesHeldTileIdToGameState()
    {
        // v1.12 Stage 2：BeginMapExpand 改為填批次，HeldTileId 由 SelectFromBatch(0) 設定。
        var (_, state, map) = NewStateBackedMap();
        var topId = state.TileDeck[0];

        map.BeginMapExpand().Should().BeTrue();
        map.SelectFromBatch(0).Should().BeTrue();

        state.CurrentPlayer.HeldTileId.Should().Be(topId);
        map.HeldTileId.Should().Be(topId);
        state.TileDeck.Should().NotContain(topId);
    }

    [Fact]
    public void StateMode_TryPlaceHeldTile_ClearsHeldTileIdInGameState()
    {
        var (_, state, map) = NewStateBackedMap();
        map.BeginMapExpand();
        map.SelectFromBatch(0);
        // 玩家在 (4,4)；放在 (4,5) 為合法相鄰格
        var ok = map.TryPlaceHeldTile(4, 5);

        ok.Should().BeTrue();
        state.CurrentPlayer.HeldTileId.Should().BeNull();
        map.HeldTileId.Should().BeNull();
    }

    [Fact]
    public void StateMode_CancelMapExpand_HeldReturnedToBatchEnd()
    {
        // v1.12 Stage 2：Cancel 改為 held 退回 TileChoiceBatch 末尾（不還回 TileDeck）。
        var (_, state, map) = NewStateBackedMap();
        map.BeginMapExpand();
        map.SelectFromBatch(0);
        var heldId = state.CurrentPlayer.HeldTileId!;
        var batchCountBefore = state.TileChoiceBatch.Count;

        map.CancelMapExpand();

        state.CurrentPlayer.HeldTileId.Should().BeNull();
        state.TileChoiceBatch.Count.Should().Be(batchCountBefore + 1);
        state.TileChoiceBatch[^1].Should().Be(heldId); // 退到末尾
    }

    // === P1-4 · CompanionHp dispatch ===

    [Fact]
    public void StateMode_CompanionHp_DispatchesToFirstCompanion()
    {
        // PR-B · CompanionHp 從 state.Companions[0].Hp 派生
        var (_, state, map) = NewStateBackedMap();
        // 確保有同伴（來自 prologue.StartingCompanionIds）
        state.Companions.Should().NotBeEmpty();
        state.Companions[0].Hp = 7;

        map.CompanionHp.Should().Be(7);
    }

    [Fact]
    public void StateMode_CompanionHp_ReturnsZero_WhenNoCompanions()
    {
        var (_, state, map) = NewStateBackedMap();
        state.Companions.Clear();

        map.CompanionHp.Should().Be(0);
    }

    [Fact]
    public void StateMode_LoadCompanion_PreservesHp_WhenSameCompanionId()
    {
        // PR-B · LoadCompanion 在 state.Companions[0] 已是同 id 時，不覆蓋 Hp（GameState 為 SoT）
        var (module, state, map) = NewStateBackedMap();
        var existingId = state.Companions[0].CompanionId;
        state.Companions[0].Hp = 3; // 戰鬥後低 HP

        var existingNpc = module.NpcCompanions[existingId];
        map.LoadCompanion(existingNpc);

        // Hp 應保留為 3（GameState 為 SoT），不被 module 預設 Hp 覆蓋
        state.Companions[0].Hp.Should().Be(3);
        map.CompanionHp.Should().Be(3);
    }

    [Fact]
    public void StateMode_LoadCompanion_ReplacesCompanionState_WhenDifferentId()
    {
        var (module, state, map) = NewStateBackedMap();
        // 找一個與當前同伴不同的 NpcCompanion
        var currentId = state.Companions[0].CompanionId;
        var otherCompanion = module.NpcCompanions.Values.First(c => c.Id != currentId);

        map.LoadCompanion(otherCompanion);

        state.Companions.Should().ContainSingle();
        state.Companions[0].CompanionId.Should().Be(otherCompanion.Id);
        state.Companions[0].Hp.Should().Be(otherCompanion.Hp);
    }

    // === P1-5 · FirstObserveUsedThisTurn dispatch ===

    [Fact]
    public void StateMode_FirstObserveUsedThisTurn_DispatchesToObservesThisTurn()
    {
        var (_, state, map) = NewStateBackedMap();
        state.CurrentPlayer.ObservesThisTurn.Should().Be(0);
        map.FirstObserveUsedThisTurn.Should().BeFalse();

        state.CurrentPlayer.ObservesThisTurn = 1;
        map.FirstObserveUsedThisTurn.Should().BeTrue();
    }

    [Fact]
    public void StateMode_AdvanceTurn_ResetsObservesThisTurn()
    {
        // AdvanceTurn 應重置 FirstObserveUsedThisTurn，state.CurrentPlayer.ObservesThisTurn 同步歸零
        var (_, state, map) = NewStateBackedMap();
        state.CurrentPlayer.ObservesThisTurn = 1;
        map.FirstObserveUsedThisTurn.Should().BeTrue();

        map.AdvanceTurn();

        state.CurrentPlayer.ObservesThisTurn.Should().Be(0);
        map.FirstObserveUsedThisTurn.Should().BeFalse();
    }
}
