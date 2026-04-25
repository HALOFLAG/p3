namespace CardNarrative.Tests;

internal static class TestPaths
{
    public static string TestRoot => AppContext.BaseDirectory;
    public static string SchemasFolder => Path.Combine(TestRoot, "schemas");
    public static string ValidModuleFolder => Path.Combine(TestRoot, "Fixtures", "valid-module");
    public static string FixturesFolder => Path.Combine(TestRoot, "Fixtures");
    public static string BuiltinModulesFolder => Path.Combine(TestRoot, "modules", "builtin");
    public static string AbandonedMansionFolder => Path.Combine(BuiltinModulesFolder, "abandoned-mansion");
}
