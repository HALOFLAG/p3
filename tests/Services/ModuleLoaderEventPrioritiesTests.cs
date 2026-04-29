// Phase 3 任務 14（S5）— Prologue.EventPriorities schema + ModuleLoader cross-validation。
// 用 valid-module clone 出 temp 後注入欄位，驗 happy path 與 unknown id 兩種。
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class ModuleLoaderEventPrioritiesTests
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

    private static void InjectIntoPrologue(string moduleFolder, string injection)
    {
        var path = Path.Combine(moduleFolder, "prologue.json");
        var text = File.ReadAllText(path);
        int lastBrace = text.LastIndexOf('}');
        text = text.Insert(lastBrace, ",\n  " + injection + "\n");
        File.WriteAllText(path, text);
    }

    [Fact]
    public void EventPriorities_AllExist_LoadSuccess()
    {
        var temp = CloneValidModuleToTemp();
        try
        {
            // valid-module 的事件 id 為 warehouse-investigation, warehouse-guard
            InjectIntoPrologue(temp, "\"eventPriorities\": [\"warehouse-investigation\"]");

            var result = NewLoader().Load(temp);

            result.Should().BeOfType<ModuleLoadResult.Success>();
            var module = ((ModuleLoadResult.Success)result).Module;
            module.Prologue.EventPriorities.Should().Equal("warehouse-investigation");
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void EventPriorities_UnknownId_LoadFailure()
    {
        var temp = CloneValidModuleToTemp();
        try
        {
            InjectIntoPrologue(temp, "\"eventPriorities\": [\"warehouse-investigation\", \"nonexistent-event\"]");

            var result = NewLoader().Load(temp);

            result.Should().BeOfType<ModuleLoadResult.Failure>();
            var fail = (ModuleLoadResult.Failure)result;
            fail.Errors.Should().Contain(e =>
                e.FilePath == "prologue.json"
                && e.JsonPointer.Contains("eventPriorities")
                && e.Message.Contains("nonexistent-event"));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void EventPriorities_Empty_NoEffect()
    {
        // valid-module 預設無 eventPriorities → Prologue.EventPriorities 應為空 list
        var module = ModuleFactory.Load();
        module.Prologue.EventPriorities.Should().BeEmpty();
    }

    [Fact]
    public void AbandonedMansion_EventPrioritiesLoadsCorrectly()
    {
        // 我們在 abandoned-mansion 加了 ["storm-investigation", "study-collapse"]
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.AbandonedMansionFolder);

        result.Should().BeOfType<ModuleLoadResult.Success>();
        var module = ((ModuleLoadResult.Success)result).Module;
        module.Prologue.EventPriorities.Should().Equal("storm-investigation", "study-collapse");
    }
}
