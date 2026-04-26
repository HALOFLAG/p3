// Phase 2 任務 11 Stage 1 — Runtime bootstrap：abandoned-mansion 模組載入 +
// GameState 建立 (gridSize=9, startPosition=(4,4)) + 起始同伴從 prologue.startingCompanionIds 取。
// 驗證：
// - prologue.json 含 startingCompanionIds = [old-priest, hired-bodyguard]
// - GameState.CreateNew 用模組真實同伴 ID 建立 → state.Companions 為這 2 位
// - 起始 tile 放在 (4,4)；玩家位置 (4,4)
// - WorldMap.LoadCompanions API 替換 placeholder 為模組真實同伴
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.State;

public class Stage1RuntimeBootstrapTests
{
    private static (Module module, GameState state) NewRuntimeState()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.AbandonedMansionFolder);
        result.Should().BeOfType<ModuleLoadResult.Success>();
        var module = ((ModuleLoadResult.Success)result).Module;

        var heroId = module.Characters.ContainsKey("scholar")
            ? "scholar"
            : module.Characters.Keys.First();
        var companionIds = module.Prologue.StartingCompanionIds.Count > 0
            ? module.Prologue.StartingCompanionIds.ToList()
            : new List<string> { "old-priest", "hired-bodyguard" };

        var state = GameState.CreateNew(
            module,
            chosenCharacterIds: new[] { heroId },
            chosenCompanionIds: companionIds,
            seed: 1234,
            gridSize: 9,
            startPosition: new Position(4, 4));
        return (module, state);
    }

    [Fact]
    public void Prologue_HasStartingCompanionIds_OldPriestAndBodyguard()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.AbandonedMansionFolder);
        var module = ((ModuleLoadResult.Success)result).Module;

        module.Prologue.StartingCompanionIds.Should().BeEquivalentTo(new[]
        {
            "old-priest",
            "hired-bodyguard"
        });
    }

    [Fact]
    public void RuntimeBootstrap_PlacesStartingTileAt4_4()
    {
        var (_, state) = NewRuntimeState();
        state.GridSize.Should().Be(9);
        state.TileMap.Keys.Should().Contain((4, 4));
        state.TileMap.Keys.Should().NotContain((0, 0));
        state.CurrentPlayer.Position.Should().Be(new Position(4, 4));
    }

    [Fact]
    public void RuntimeBootstrap_LoadsTwoCompanionsFromPrologue()
    {
        var (_, state) = NewRuntimeState();
        state.Companions.Should().HaveCount(2);
        state.Companions.Select(c => c.CompanionId).Should().BeEquivalentTo(new[]
        {
            "old-priest",
            "hired-bodyguard"
        });
    }

    [Fact]
    public void StartingTile_AtPlayerPos_IsModulePrologueStartingTile()
    {
        var (module, state) = NewRuntimeState();
        var pos = state.CurrentPlayer.Position;
        state.TileMap[(pos.X, pos.Y)].TileId.Should().Be(module.Prologue.StartingTileId);
    }

    [Fact]
    public void WorldMap_LoadCompanions_ReplacesPlaceholdersWithModuleCompanions()
    {
        var (module, _) = NewRuntimeState();
        var map = new WorldMap();
        // 起始 _companions 是 placeholder companion-a / companion-b
        map.Companions.Should().HaveCount(2);
        map.Companions.Select(c => c.CompanionId).Should().BeEquivalentTo(
            new[] { "companion-a", "companion-b" });

        var realCompanions = new[]
        {
            module.NpcCompanions["old-priest"],
            module.NpcCompanions["hired-bodyguard"]
        };
        map.LoadCompanions(realCompanions);

        map.Companions.Should().HaveCount(2);
        map.Companions.Select(c => c.CompanionId).Should().BeEquivalentTo(
            new[] { "old-priest", "hired-bodyguard" });
        map.Companions.Should().AllSatisfy(c => c.RemainingAp.Should().Be(WorldMap.CompanionApMax));
    }

    [Fact]
    public void WorldMap_LoadCompanions_LimitsToTwo()
    {
        var (module, _) = NewRuntimeState();
        var map = new WorldMap();

        // 餵 3 個同伴，預期只取前 2 個
        var threeCompanions = module.NpcCompanions.Values.Take(3).ToList();
        threeCompanions.Should().HaveCount(3); // sanity check abandoned-mansion 有 ≥3 同伴

        map.LoadCompanions(threeCompanions);

        map.Companions.Should().HaveCount(2);
    }
}
