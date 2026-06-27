using FabioSoft.Marketplace;
using FabioSoft.Marketplace.Io;
using FabioSoft.Nucleus.Kernel;

namespace FabioSoft.Nucleus.Plugins.MarketplacePlugin;

/// Bridges the lifecycle pipeline to the in-process build engine (Roslyn/FCS) - the only build path. The
/// host publishes the reference roots as environment variables (the same ones the kernel uses), so the
/// pipeline resolves them without new wiring.
internal static class InProcessGate
{
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

    // Runs a compiled test assembly via the host-shipped out-of-process test host (a clean child process),
    // probing the plugin-under-test's output and the shared roots for the test's dependencies. Exit code
    // 0 = passed.
    public static (bool ok, string output) RunTest(string testAssemblyPath, string pluginOutputDir)
    {
        var exeDir = Environment.GetEnvironmentVariable("ClavisExeDir") ?? AppContext.BaseDirectory;
        var testHost = Path.Combine(exeDir, "testhost", "FabioSoft.TestHost.exe");
        if (!File.Exists(testHost))
            return (false, $"out-of-process test host not found: {testHost}");

        var startInfo = new System.Diagnostics.ProcessStartInfo(testHost)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(testAssemblyPath);
        startInfo.ArgumentList.Add(pluginOutputDir);
        foreach (var root in ReferenceRoots())
            startInfo.ArgumentList.Add(root);

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode == 0, process.ExitCode == 0 ? output : output + error);
    }
}
