// Phase 3 v1.12 Stage 3 — Prologue tileBatches schema + ModuleLoader 驗證 + BeginMapExpand 取批次順序。
// 規格書 §1.5 / §3.1.4。
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class ModuleLoaderTileBatchesTests
{
    private sealed class NoSubstituteRandom : IRandomProvider
    {
        public double NextDouble() => 0.99;
        public int Next(int maxExclusive) => 0;
    }

    private ModuleLoader NewLoader() => new(TestPaths.SchemasFolder);

    private string CloneValidModuleToTemp()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cn-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        foreach (var src in Directory.GetFiles(TestPaths.ValidModuleFolder))
        {
            File.Copy(src, Path.Combine(temp, Path.GetFileName(src)));
        }
        return temp;
    }

    /// <summary>把 prologue.json 的最後一個 } 之前插入新欄位。</summary>
    private static void InjectIntoPrologue(string moduleFolder, string injection)
    {
        var path = Path.Combine(moduleFolder, "prologue.json");
        var text = File.ReadAllText(path);
        int lastBrace = text.LastIndexOf('}');
        text = text.Insert(lastBrace, ",\n  " + injection + "\n");
        File.WriteAllText(path, text);
    }

    [Fact]
    public void ModuleLoader_TileBatches_ParsesCorrectly()
    {
        var temp = CloneValidModuleToTemp();
        // valid-module 只 3 個 tile：town-square (start) / wilderness-path / warehouse
        InjectIntoPrologue(temp,
            "\"tileBatches\": [[\"wilderness-path\"], [\"warehouse\"]]");

        var result = NewLoader().Load(temp);

        result.Should().BeOfType<ModuleLoadResult.Success>();
        var prologue = result.ModuleOrNull!.Prologue;
        prologue.TileBatches.Should().HaveCount(2);
        prologue.TileBatches[0].Should().ContainSingle().Which.Should().Be("wilderness-path");
        prologue.TileBatches[1].Should().ContainSingle().Which.Should().Be("warehouse");
    }

    [Fact]
    public void ModuleLoader_TileBatchesInvalidSize_ThrowsException()
    {
        var temp = CloneValidModuleToTemp();
        // 4 張 batch 違反 schema maxItems=3
        InjectIntoPrologue(temp,
            "\"tileBatches\": [[\"wilderness-path\", \"warehouse\", \"wilderness-path\", \"warehouse\"]]");

        var result = NewLoader().Load(temp);

        result.Should().BeOfType<ModuleLoadResult.Failure>();
        result.ErrorsOrEmpty.Should().Contain(e => e.FilePath == "prologue.json");
    }

    [Fact]
    public void ModuleLoader_TileBatchesUnknownTileId_ThrowsException()
    {
        var temp = CloneValidModuleToTemp();
        InjectIntoPrologue(temp,
            "\"tileBatches\": [[\"nonexistent-tile\"]]");

        var result = NewLoader().Load(temp);

        result.Should().BeOfType<ModuleLoadResult.Failure>();
        result.ErrorsOrEmpty.Should().Contain(e =>
            e.FilePath == "prologue.json"
            && e.Message.Contains("nonexistent-tile"));
    }

    [Fact]
    public void BeginMapExpand_PrologueBatchesPresent_DrawsFromBatchInOrder()
    {
        var temp = CloneValidModuleToTemp();
        InjectIntoPrologue(temp,
            "\"tileBatches\": [[\"wilderness-path\", \"warehouse\"], [\"warehouse\"]]");

        var module = ((ModuleLoadResult.Success)NewLoader().Load(temp)).Module;
        var heroId = module.Characters.Keys.First();
        var state = GameState.CreateNew(
            module,
            chosenCharacterIds: new[] { heroId },
            chosenCompanionIds: System.Array.Empty<string>(),
            seed: 42,
            gridSize: 11,
            startPosition: new Position(5, 5));

        // CreateNew 應把 prologue.TileBatches 拷貝到 PendingTileBatches，並跳過 TileDeck 機械填充
        state.PendingTileBatches.Should().HaveCount(2);
        state.PendingTileBatches[0].Should().Equal("wilderness-path", "warehouse");
        state.TileDeck.Should().BeEmpty(); // 批次模式下 TileDeck 不再灌入

        var map = new WorldMap(state, module, new NoSubstituteRandom());

        // 第一次 BeginMapExpand 應消耗 PendingTileBatches[0]
        map.BeginMapExpand().Should().BeTrue();
        state.TileChoiceBatch.Should().Equal("wilderness-path", "warehouse");
        state.PendingTileBatches.Should().HaveCount(1); // 第一組已被取走
        state.PendingTileBatches[0].Should().Equal("warehouse");
    }
}
