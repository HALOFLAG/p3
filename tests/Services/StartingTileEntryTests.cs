using System.Text.Json;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

/// <summary>
/// Tile.OnEnter effects (e.g. mansion-foyer's setFlag) and tileEnter-trigger events
/// must fire for the starting tile when the game begins, just like any other tile the
/// player walks into. Previously the starting room's onEnter was silently skipped.
/// </summary>
public class StartingTileEntryTests
{
    private const string StartId = "start";
    private const string FillerId = "filler";

    private static Module BuildModuleWithStartOnEnter()
    {
        var manifest = new Manifest("test", "Test", "1.0", "", 1, "", "");
        var prologue = new Prologue(
            "Test", 1, "",
            new List<WinCondition>(),
            new List<LoseCondition>(),
            new DifficultyCurve(
                new DifficultyRange(new[] { 8, 10 }),
                new DifficultyRange(new[] { 10, 12 }),
                new ClimaxCurve(14, 1, 18)),
            StartId,
            Array.Empty<string>(),
            0,
            "contributions * 2 + equipmentSlotsFilled");

        var trueFlag = JsonDocument.Parse("true").RootElement.Clone();
        var onEnter = new List<EffectBase>
        {
            new SetFlagEffect("entered_start", trueFlag),
            new GrantResourceEffect("welcome_token", 1),
        };
        var tiles = new Dictionary<string, Tile>
        {
            [StartId] = new Tile(StartId, "Start", Terrain.Town, false,
                Array.Empty<ActionType>(), Array.Empty<TileResource>(), onEnter),
            [FillerId] = new Tile(FillerId, "Filler", Terrain.Town, false,
                Array.Empty<ActionType>(), Array.Empty<TileResource>(), Array.Empty<EffectBase>()),
        };
        var characters = new Dictionary<string, Character>
        {
            ["hero"] = new Character("hero", "Hero", new StatBlock(1, 1, 1, 1), 10, "", Array.Empty<string>()),
        };
        return new Module(
            manifest, prologue, characters,
            new Dictionary<string, NpcCompanion>(),
            tiles,
            new Dictionary<string, EventCard>(),
            new Dictionary<string, ActionCard>(),
            new Dictionary<string, Equipment>(),
            new Dictionary<string, Ending>(),
            new Dictionary<string, BattleCard>());
    }

    [Fact]
    public void TriggerStartingTileEntry_Applies_OnEnter_Effects()
    {
        var module = BuildModuleWithStartOnEnter();
        var state = GameState.CreateNew(module, new[] { "hero" }, Array.Empty<string>(), seed: 1);
        var loop = new TurnLoop(state, new FakeDiceService(), module);

        // Before entry, flag is absent.
        state.Flags.Should().NotContainKey("entered_start");
        state.Resources.GetValueOrDefault("welcome_token").Should().Be(0);

        loop.TriggerStartingTileEntry();

        state.Flags.Should().ContainKey("entered_start");
        state.Resources["welcome_token"].Should().Be(1);
    }

    [Fact]
    public void TriggerStartingTileEntry_Idempotent_ForSameBigRound()
    {
        // Calling twice shouldn't double-apply the grantResource effect.
        var module = BuildModuleWithStartOnEnter();
        var state = GameState.CreateNew(module, new[] { "hero" }, Array.Empty<string>(), seed: 1);
        var loop = new TurnLoop(state, new FakeDiceService(), module);

        loop.TriggerStartingTileEntry();
        loop.TriggerStartingTileEntry();

        state.Resources["welcome_token"].Should().Be(1); // not 2
    }

    [Fact]
    public void Move_AlsoApplies_OnEnter_Effects()
    {
        // Parity check: walking into a tile should apply its OnEnter effects identically.
        var module = BuildModuleWithStartOnEnter();
        var state = GameState.CreateNew(module, new[] { "hero" }, Array.Empty<string>(), seed: 1);
        // Swap: put filler at (0,0) (no effects) and the effect-carrying tile at (1,0).
        state.TileMap.Clear();
        state.TileMap[(0, 0)] = new PlacedTile { TileId = FillerId, Level = ExplorationLevel.Unfamiliar };
        state.TileMap[(1, 0)] = new PlacedTile { TileId = StartId, Level = ExplorationLevel.Unknown };
        state.CurrentPlayer.Position = new Position(0, 0);
        state.Phase = TurnPhase.Action;
        var loop = new TurnLoop(state, new FakeDiceService(), module);

        loop.Move(new Position(1, 0));

        state.Flags.Should().ContainKey("entered_start");
        state.Resources["welcome_token"].Should().Be(1);
    }
}
