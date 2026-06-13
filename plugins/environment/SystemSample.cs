using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FabioSoft.Nucleus.Plugins.Environment;

/// Samples CPU (the Clavis process, averaged over the interval between reads), system RAM load, and the
/// working-directory volume's disk usage. Impure and Windows-leaning; excluded from coverage.
[ExcludeFromCodeCoverage]
internal sealed class SystemSample
{
    private TimeSpan _lastCpuTime;
    private DateTime _lastSampledAt;

    public SystemSample()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        _lastCpuTime = process.TotalProcessorTime;
        _lastSampledAt = DateTime.UtcNow;
    }

    public IReadOnlyDictionary<string, string> Read(string workingDirectory)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ReadCpu(values);
        ReadMemory(values);
        ReadDisk(values, workingDirectory);

        return values;
    }

    private void ReadCpu(Dictionary<string, string> values)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var now = DateTime.UtcNow;
        var cpuMilliseconds = (process.TotalProcessorTime - _lastCpuTime).TotalMilliseconds;
        var wallMilliseconds = (now - _lastSampledAt).TotalMilliseconds * System.Environment.ProcessorCount;

        _lastCpuTime = process.TotalProcessorTime;
        _lastSampledAt = now;

        if (wallMilliseconds > 0)
        {
            values["sys.cpu"] = Percent(100.0 * cpuMilliseconds / wallMilliseconds);
        }
    }

    private static void ReadMemory(Dictionary<string, string> values)
    {
        var memory = GC.GetGCMemoryInfo();
        if (memory.TotalAvailableMemoryBytes <= 0)
        {
            return;
        }

        values["sys.ram"] = Percent(100.0 * memory.MemoryLoadBytes / memory.TotalAvailableMemoryBytes);
        values["sys.ramUsed"] = Gigabytes(memory.MemoryLoadBytes);
        values["sys.ramTotal"] = Gigabytes(memory.TotalAvailableMemoryBytes);
    }

    private static void ReadDisk(Dictionary<string, string> values, string workingDirectory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(workingDirectory));
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return;
            }

            var used = drive.TotalSize - drive.AvailableFreeSpace;
            values["sys.disk"] = Percent(100.0 * used / drive.TotalSize);
            values["sys.diskFree"] = Gigabytes(drive.AvailableFreeSpace);
        }
        catch (IOException)
        {
        }
    }

    private static string Percent(double value) =>
        ((int)Math.Round(Math.Clamp(value, 0, 100))).ToString(CultureInfo.InvariantCulture);

    private static string Gigabytes(long bytes) =>
        (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
}
