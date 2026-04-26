// Phase 2 任務 11 Stage 2b — WorldMap dual-mode（state-backed）測試。
// 驗證：
// - state-backed ctor 接受 GameState + Module
// - PlayerPos / Hp / Ap / Turn getter dispatch 到 GameState
// - 透過 WorldMap mutation API 改 PlayerPos/Hp/Ap/Turn 會寫進 GameState（單一 SoT）
// - FirstMoveUsedThisTurn 從 PlayerState.MovesThisTurn > 0 派生
// - HpMax 在 state-mode 是 read-only（init-only 契約）
// - standalone mode（既有 ctor）行為不變
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapDualModeTests
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

    // === Ctor / BackingState ===

    [Fact]
    public void StateBackedCtor_StoresStateAndModuleRefs()
    {
        var (module, state, map) = NewStateBackedMap();
        map.BackingState.Should().BeSameAs(state);
        map.BackingModule.Should().BeSameAs(module);
    }

    [Fact]
    public void StandaloneCtor_HasNoBackingState()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        map.BackingState.Should().BeNull();
        map.BackingModule.Should().BeNull();
    }

    // === Getter dispatch — state-mode reads GameState ===

    [Fact]
    public void StateMode_PlayerPos_ReadsFromGameState()
    {
        var (_, state, map) = NewStateBackedMap();
        // CreateNew 把 startPosition (4,4) 設成 Position(X=4, Y=4)
        state.CurrentPlayer.Position.Should().Be(new Position(4, 4));
        // WorldMap.PlayerPos 是 (Row, Col)，state 是 Position(X, Y)；對應 X→Col, Y→Row
        map.PlayerPos.Should().Be((4, 4));
    }

    [Fact]
    public void StateMode_PlayerPos_ChangesWhenStateChanges()
    {
        var (_, state, map) = NewStateBackedMap();
        state.CurrentPlayer.Position = new Position(7, 3);
        map.PlayerPos.Should().Be((3, 7)); // Row=Y=3, Col=X=7
    }

    [Fact]
    public void StateMode_Hp_ReadsFromGameState()
    {
        var (module, state, map) = NewStateBackedMap();
        var character = module.Characters[state.CurrentPlayer.CharacterId];
        map.Hp.Should().Be(character.HpMax);
        map.HpMax.Should().Be(character.HpMax);

        state.CurrentPlayer.Hp = 5;
        map.Hp.Should().Be(5);
    }

    [Fact]
    public void StateMode_Ap_ReadsFromGameState()
    {
        var (_, state, map) = NewStateBackedMap();
        map.Ap.Should().Be(WorldMap.ApMax);

        state.CurrentPlayer.ActionPoints = 1;
        map.Ap.Should().Be(1);
    }

    [Fact]
    public void StateMode_Turn_ReadsFromGameState()
    {
        var (_, state, map) = NewStateBackedMap();
        map.Turn.Should().Be(1); // CurrentBigRound 預設 1

        state.CurrentBigRound = 5;
        map.Turn.Should().Be(5);
    }

    [Fact]
    public void StateMode_FirstMoveUsedThisTurn_DerivedFromMovesThisTurn()
    {
        var (_, state, map) = NewStateBackedMap();
        state.CurrentPlayer.MovesThisTurn.Should().Be(0);
        map.FirstMoveUsedThisTurn.Should().BeFalse();

        state.CurrentPlayer.MovesThisTurn = 1;
        map.FirstMoveUsedThisTurn.Should().BeTrue();
    }

    // === Mutation dispatch — state-mode writes GameState ===

    [Fact]
    public void StateMode_TryConsumeAp_WritesToGameState()
    {
        var (_, state, map) = NewStateBackedMap();
        state.CurrentPlayer.ActionPoints = 3;

        map.TryConsumeAp(2).Should().BeTrue();

        state.CurrentPlayer.ActionPoints.Should().Be(1);
        map.Ap.Should().Be(1);
    }

    [Fact]
    public void StateMode_Rest_HpAndApWriteToGameState()
    {
        var (_, state, map) = NewStateBackedMap();
        state.CurrentPlayer.Hp = 3;
        state.CurrentPlayer.ActionPoints = 2;

        map.Rest();

        state.CurrentPlayer.Hp.Should().Be(5); // +2 (clamped to HpMax)
        state.CurrentPlayer.ActionPoints.Should().Be(0);
    }

    [Fact]
    public void StateMode_HpMax_IsReadOnly_InitOnlyContract()
    {
        // HpMax 在 PlayerState 是 init-only；state-mode WorldMap 試圖修改視為 no-op
        var (module, state, map) = NewStateBackedMap();
        var origHpMax = state.CurrentPlayer.HpMax;

        var character = module.Characters[state.CurrentPlayer.CharacterId];
        map.LoadCharacter(character); // 內部會試圖設 HpMax；state-mode 應 no-op

        state.CurrentPlayer.HpMax.Should().Be(origHpMax);
        map.HpMax.Should().Be(origHpMax);
    }

    // === Standalone mode — 行為不變 ===

    [Fact]
    public void StandaloneMode_PlayerPos_UsesInternalField()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        map.PlayerPos.Should().Be((4, 4)); // InitialPlayerRow, InitialPlayerCol
        // standalone mode 沒有 GameState，內部欄位為 source
    }

    [Fact]
    public void StandaloneMode_HpMax_CanBeSetByLoadCharacter()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        var c = new Character("test", "T", new StatBlock(1, 1, 1, 1), 20, "", System.Array.Empty<string>());
        map.LoadCharacter(c);
        map.HpMax.Should().Be(20);
        map.Hp.Should().Be(20);
    }

    [Fact]
    public void StandaloneMode_Ap_DefaultsToApMax()
    {
        var map = new WorldMap(new NoSubstituteRandom());
        map.Ap.Should().Be(WorldMap.ApMax);
    }
}
