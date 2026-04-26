using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests;

public class ModuleLoaderTests
{
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

    [Fact]
    public void Load_ValidModule_ReturnsSuccessWithAllCardTypes()
    {
        var result = NewLoader().Load(TestPaths.ValidModuleFolder);

        result.Should().BeOfType<ModuleLoadResult.Success>();
        var module = result.ModuleOrNull!;
        module.Manifest.Id.Should().Be("test-valid");
        module.Characters.Should().HaveCountGreaterOrEqualTo(1);
        module.NpcCompanions.Should().HaveCountGreaterOrEqualTo(1);
        module.Tiles.Should().HaveCountGreaterOrEqualTo(1);
        module.Events.Should().HaveCountGreaterOrEqualTo(1);
        module.ActionCards.Should().HaveCountGreaterOrEqualTo(4);
        module.Equipment.Should().HaveCountGreaterOrEqualTo(1);
        module.Endings.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Fact]
    public void Load_ValidModule_DeserializesCharacterCardFields()
    {
        var module = NewLoader().Load(TestPaths.ValidModuleFolder).ModuleOrNull!;
        var hunter = module.Characters["wild-hunter"];
        hunter.Description.Should().NotBeNullOrEmpty();
        hunter.Skills.Should().HaveCount(2);
        hunter.Skills[0].Kind.Should().Be(SkillKind.Active);
        hunter.Skills[1].Kind.Should().Be(SkillKind.Passive);
    }

    [Fact]
    public void Load_ValidModule_DeserializesCompanionCardFields()
    {
        var module = NewLoader().Load(TestPaths.ValidModuleFolder).ModuleOrNull!;
        var leia = module.NpcCompanions["companion-thief"];
        leia.Description.Should().NotBeNullOrEmpty();
        leia.Personality.Should().NotBeNullOrEmpty();
        leia.SceneResponses.Should().HaveCount(2);
        leia.SceneResponses[0].Scene.Should().Be("exploration");
        leia.Skills.Should().HaveCount(1);
    }

    [Fact]
    public void Load_MissingManifest_ReturnsFailure()
    {
        var temp = CloneValidModuleToTemp();
        File.Delete(Path.Combine(temp, "manifest.json"));

        var result = NewLoader().Load(temp);

        result.Should().BeOfType<ModuleLoadResult.Failure>();
        result.ErrorsOrEmpty.Should().Contain(e => e.FilePath == "manifest.json");
    }

    [Fact]
    public void Load_SchemaViolation_ReportsFilePathAndPointer()
    {
        var temp = CloneValidModuleToTemp();
        var tilesPath = Path.Combine(temp, "tiles.json");
        var text = File.ReadAllText(tilesPath).Replace("\"wilderness\"", "\"desert\"");
        File.WriteAllText(tilesPath, text);

        var result = NewLoader().Load(temp);

        result.Should().BeOfType<ModuleLoadResult.Failure>();
        result.ErrorsOrEmpty
            .Should().Contain(e => e.FilePath == "tiles.json");
    }

    [Fact]
    public void Load_BrokenCrossReference_ReportsError()
    {
        var temp = CloneValidModuleToTemp();
        var prologuePath = Path.Combine(temp, "prologue.json");
        var text = File.ReadAllText(prologuePath).Replace("\"town-square\"", "\"nonexistent-tile\"");
        File.WriteAllText(prologuePath, text);

        var result = NewLoader().Load(temp);

        result.Should().BeOfType<ModuleLoadResult.Failure>();
        result.ErrorsOrEmpty
            .Should().Contain(e =>
                e.FilePath == "prologue.json" &&
                e.Message.Contains("startingTileId"));
    }

    [Fact]
    public void Load_InvalidEffectDiscriminator_ReportsError()
    {
        var temp = CloneValidModuleToTemp();
        var eventsPath = Path.Combine(temp, "events.json");
        var text = File.ReadAllText(eventsPath)
            .Replace("\"type\": \"setFlag\"", "\"type\": \"unknownEffect\"");
        File.WriteAllText(eventsPath, text);

        var result = NewLoader().Load(temp);

        result.Should().BeOfType<ModuleLoadResult.Failure>();
        result.ErrorsOrEmpty.Should().Contain(e => e.FilePath == "events.json");
    }

    [Fact]
    public void ValidationError_ToString_IncludesFileAndPointer()
    {
        var err = new ValidationError("tiles.json", "/0/terrain", "value 'desert' is not in enum");
        err.ToString().Should().Contain("tiles.json");
        err.ToString().Should().Contain("/0/terrain");
        err.ToString().Should().Contain("desert");
    }
}
