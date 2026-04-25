using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;

namespace CardNarrative.Tests.Services;

internal static class ModuleFactory
{
    public static Module Load()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.ValidModuleFolder);
        return ((ModuleLoadResult.Success)result).Module;
    }

    public static GameState NewState(Module module, int seed = 1234, int players = 2)
    {
        var chars = module.Characters.Keys.Take(players).ToList();
        var comps = module.NpcCompanions.Keys.Take(1).ToList();
        return GameState.CreateNew(module, chars, comps, seed);
    }
}
