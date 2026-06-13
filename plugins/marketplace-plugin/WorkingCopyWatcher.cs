using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.MarketplacePlugin;

/// Watches each marketplace working copy's plugins/ and shared/ groups for changes and, after a quiet
/// debounce, hands the changed item directory to a callback. Coalesces edit bursts per item, ignores build
/// output (bin/obj), and suppresses an item while its pipeline runs (and briefly after) so the pipeline's
/// own PLUGIN.md / surface.json writes do not retrigger it. This is the single mechanism behind both
/// triggers: an edit by the developer and a self-edit by Clavis are both just on-disk writes.
internal sealed class WorkingCopyWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(3);
    private static readonly string[] Groups = ["plugins", "modules"];

    private readonly Func<string, Task> onChanged;
    private readonly Action<string> log;
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly object gate = new();
    private readonly Dictionary<string, Timer> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> suppressed = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;

    public WorkingCopyWatcher(IEnumerable<string> workingCopies, Func<string, Task> onChanged, Action<string> log)
    {
        this.onChanged = onChanged;
        this.log = log;
        foreach (var workingCopy in workingCopies)
            foreach (var group in Groups)
            {
                var groupDir = Path.Combine(workingCopy, group);
                if (Directory.Exists(groupDir))
                    Watch(groupDir);
            }
    }

    private void Watch(string groupDir)
    {
        var watcher = new FileSystemWatcher(groupDir)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
        };
        watcher.Changed += (_, e) => OnEvent(groupDir, e.FullPath);
        watcher.Created += (_, e) => OnEvent(groupDir, e.FullPath);
        watcher.Deleted += (_, e) => OnEvent(groupDir, e.FullPath);
        watcher.Renamed += (_, e) => OnEvent(groupDir, e.FullPath);
        watcher.Error += (_, e) => log($"watch error under {groupDir}: {e.GetException().Message}");
        watcher.EnableRaisingEvents = true;
        watchers.Add(watcher);
    }

    private void OnEvent(string groupDir, string fullPath)
    {
        if (IsBuildOutput(groupDir, fullPath))
            return;
        var itemDir = ItemDirOf(groupDir, fullPath);
        if (itemDir is not null)
            Schedule(itemDir);
    }

    // The item directory is the immediate child of the group dir on the changed path.
    private static string? ItemDirOf(string groupDir, string fullPath)
    {
        if (!fullPath.StartsWith(groupDir, StringComparison.OrdinalIgnoreCase))
            return null;
        var first = fullPath[groupDir.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrEmpty(first) ? null : Path.Combine(groupDir, first);
    }

    private static bool IsBuildOutput(string groupDir, string fullPath)
    {
        var segments = fullPath[groupDir.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(s => s is "bin" or "obj");
    }

    private void Schedule(string itemDir)
    {
        lock (gate)
        {
            if (disposed || suppressed.Contains(itemDir))
                return;
            if (pending.TryGetValue(itemDir, out var existing))
                existing.Change(Debounce, Timeout.InfiniteTimeSpan);
            else
                pending[itemDir] = new Timer(_ => Fire(itemDir), null, Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire(string itemDir)
    {
        lock (gate)
        {
            if (disposed)
                return;
            if (pending.Remove(itemDir, out var timer))
                timer.Dispose();
            if (!suppressed.Add(itemDir))
                return;
        }

        // Run the pipeline off the timer thread, then hold the item suppressed through a cooldown so the
        // pipeline's own writes (PLUGIN.md, surface.json) settle without retriggering.
        _ = Task.Run(async () =>
        {
            try
            {
                await onChanged(itemDir);
            }
            catch (Exception ex)
            {
                log($"pipeline error for {Path.GetFileName(itemDir)}: {ex.Message}");
            }

            await Task.Delay(Cooldown);
            lock (gate)
                suppressed.Remove(itemDir);
        });
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (var watcher in watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            watchers.Clear();
            foreach (var timer in pending.Values)
                timer.Dispose();
            pending.Clear();
        }
    }
}
