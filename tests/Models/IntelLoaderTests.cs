// IntelLoaderTests — 驗證 intel.json 載入路徑（Phase 3 任務 14 / S4）。
//
// 涵蓋：
//   - valid-module 測試模組（無 intel.json）→ Module.Intel 為空 dict
//   - abandoned-mansion 模組（含 intel.json）→ 載入 underground-passage，UnlocksTags 正確
//   - 模組驗證通過（所有 grantIntel 引用 id 都存在）
using CardNarrative.Core.Services;
using CardNarrative.Tests.Services; // ModuleFactory
using FluentAssertions;

namespace CardNarrative.Tests.Models;

public class IntelLoaderTests
{
    [Fact]
    public void TestModule_NoIntelFile_LoadsEmptyDict()
    {
        var module = ModuleFactory.Load();
        module.Intel.Should().NotBeNull();
        module.Intel.Should().BeEmpty();
    }

    [Fact]
    public void AbandonedMansion_LoadsIntelJson()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.AbandonedMansionFolder);

        result.Should().BeOfType<ModuleLoadResult.Success>();
        var module = ((ModuleLoadResult.Success)result).Module;

        module.Intel.Should().ContainKey("underground-passage");
        var intel = module.Intel["underground-passage"];
        intel.Name.Should().Be("教堂下的地下道");
        intel.UnlocksTags.Should().ContainSingle().Which.Should().Be("underground");
    }

    [Fact]
    public void AbandonedMansion_GrantIntelReferenceValid()
    {
        // 確保 chapel-investigation 的 grantIntel 引用通過 ModuleLoader cross-validation。
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.AbandonedMansionFolder);

        result.Should().BeOfType<ModuleLoadResult.Success>();
    }
}
