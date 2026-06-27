using FabioSoft.Marketplace;
using FabioSoft.Marketplace.Io;
using FabioSoft.Nucleus.Kernel;

namespace FabioSoft.Nucleus.Plugins.MarketplacePlugin;

/// Bridges the lifecycle pipeline to the in-process build engine when ClavisInProcessBuild is on. The host
/// publishes the reference roots as environment variables (the same ones the kernel uses), so the pipeline
/// resolves them without new wiring.
internal static class InProcessGate
{
    public static bool Enabled => Environment.GetEnvironmentVariable("CLAVIS_INPROCESS_BUILD") == "1";

    public static string[] ReferenceRoots() =>
        new[]
        {
            Environment.GetEnvironmentVariable("ClavisExeDir"),
            Environment.GetEnvironmentVariable("ClavisLibrariesDir"),
            Environment.GetEnvironmentVariable("ClavisModulesDir"),
        }
        .Where(root => !string.IsNullOrEmpty(root))
        .Select(root => root!)
        .ToArray();

    // Compile an item in-process from its PLUGIN.md frontmatter, returning the same CompilationResult shape
    // as PluginCompiler.compile so callers branch identically.
    public static CompilationResult Compile(string itemDir, string buildDir)
    {
        var kind = PluginKindModule.ofItemDirectory(itemDir);
        if (kind.IsError)
            return CompilationResult.NewCompilationFailed(kind.ErrorValue);

        var manifest = Path.Combine(itemDir, "PLUGIN.md");
        var spec = ItemBuildSpec.fromMarkdown(kind.ResultValue, File.ReadAllText(manifest));
        if (spec.IsError)
            return CompilationResult.NewCompilationFailed(spec.ErrorValue);

        return PluginCompiler.compileInProcessArray(spec.ResultValue, itemDir, buildDir, ReferenceRoots());
    }
}
