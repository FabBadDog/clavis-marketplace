using System.Windows;
using System.Windows.Controls;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

internal sealed class RegionManager
{
    private readonly Dictionary<string, ContentPresenter> _regions = new();
    private readonly Dictionary<string, List<RegionEntry>> _contributions = new();

    public void DefineRegion(string regionId, ContentPresenter presenter)
    {
        _regions[regionId] = presenter;
        _contributions[regionId] = [];
    }

    public void AddContribution(UiRegionContribution contribution)
    {
        if (!_regions.TryGetValue(contribution.RegionId, out var presenter))
        {
            return;
        }

        if (!_contributions.TryGetValue(contribution.RegionId, out var entries))
        {
            return;
        }

        // A re-contribution from the same plugin replaces its previous entry: after a plugin hot-reload
        // the old entry's element belongs to the disposed instance and would otherwise keep being
        // displayed (its view model never updates again) while the live view stays invisible.
        foreach (var stale in entries.Where(e => e.PluginId == contribution.PluginId))
        {
            if (stale.Resources is not null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(stale.Resources);
            }
        }

        entries.RemoveAll(e => e.PluginId == contribution.PluginId);

        var element = (FrameworkElement)contribution.ViewFactory();

        var resources = contribution.Resources as ResourceDictionary;
        if (resources is not null)
        {
            Application.Current.Resources.MergedDictionaries.Add(resources);
        }

        var entry = new RegionEntry(contribution.PluginId, contribution.Priority, element, resources);
        entries.Add(entry);
        // OrderBy is a stable sort (List.Sort is not), so equal-priority entries keep arrival order
        // and the displayed winner does not flip arbitrarily between rebuilds.
        var ordered = entries.OrderByDescending(e => e.Priority).ToList();
        entries.Clear();
        entries.AddRange(ordered);

        presenter.Content = entries[0].Element;
    }

    public void RemoveContribution(UiRegionRemoved removal)
    {
        if (!_regions.TryGetValue(removal.RegionId, out var presenter))
        {
            return;
        }

        if (!_contributions.TryGetValue(removal.RegionId, out var entries))
        {
            return;
        }

        // Remove the plugin's merged resource dictionaries from the application too, or they accumulate
        // in Application.Current.Resources across plugin reloads.
        foreach (var entry in entries)
        {
            if (entry.PluginId == removal.PluginId && entry.Resources is not null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(entry.Resources);
            }
        }

        entries.RemoveAll(e => e.PluginId == removal.PluginId);

        presenter.Content = entries.Count > 0 ? entries[0].Element : null;
    }

    private sealed record RegionEntry(string PluginId, int Priority, FrameworkElement Element, ResourceDictionary? Resources);
}
