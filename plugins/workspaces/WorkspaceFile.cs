using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// Reads and writes the workspace list as this plugin's section of `configuration.yaml`. The workspace list is
/// **durable user intent** - name, accent, working directory, slot - so it belongs in configuration, not in the
/// disposable `state.yaml` the layout lives in. Only then does the documented contract actually hold: deleting
/// `state.yaml` loses your dockings and keeps your workspaces.
///
/// Nothing live is persisted: sessions and activity are runtime facts that are re-derived on the next launch.
public static class WorkspaceFile
{
    /// Parse the section into a set. Unreadable YAML throws (the caller logs and falls back to a default set);
    /// an individual entry without a usable identity is dropped rather than failing the whole file.
    public static WorkspaceSet Parse(string? yaml, string defaultWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return WorkspaceSet.Empty;
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var document = deserializer.Deserialize<WorkspacesDocument>(yaml);
        if (document?.Workspaces is not { } entries)
        {
            return WorkspaceSet.Empty;
        }

        var workspaces = new List<Workspace>(entries.Count);
        var takenSlots = new HashSet<int>();
        foreach (var entry in entries)
        {
            if (!Guid.TryParse(entry.Id, out var workspaceId) || workspaceId == Guid.Empty)
            {
                continue;
            }

            // A duplicated or out-of-range slot in a hand-edited file would give two workspaces one F-key, so
            // the later one is demoted to slotless rather than shadowing the first.
            var slot = entry.Slot is >= 1 and <= WorkspaceSet.SlotCount && takenSlots.Add(entry.Slot)
                ? entry.Slot
                : 0;

            workspaces.Add(new Workspace
            {
                WorkspaceId = workspaceId,
                Name = string.IsNullOrWhiteSpace(entry.Name) ? $"Workspace {slot}" : entry.Name!,
                AccentKey = AccentPalette.OrDefault(entry.Accent),
                WorkingDirectory = string.IsNullOrWhiteSpace(entry.WorkingDirectory)
                    ? defaultWorkingDirectory
                    : entry.WorkingDirectory!,
                Slot = slot
            });
        }

        if (workspaces.Count == 0)
        {
            return WorkspaceSet.Empty;
        }

        // The persisted active workspace, or the first in slot order when the file names one that is gone.
        var active = Guid.TryParse(document.Active, out var activeId)
                     && workspaces.Any(workspace => workspace.WorkspaceId == activeId)
            ? activeId
            : workspaces.OrderBy(workspace => workspace.Slot <= 0).ThenBy(workspace => workspace.Slot)
                .First().WorkspaceId;

        return new WorkspaceSet { Workspaces = workspaces, ActiveWorkspaceId = active };
    }

    public static string Serialize(WorkspaceSet set)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        return serializer.Serialize(new WorkspacesDocument
        {
            Active = set.ActiveWorkspaceId == Guid.Empty ? null : set.ActiveWorkspaceId.ToString(),
            Workspaces = set.InSlotOrder()
                .Select(workspace => new WorkspaceEntry
                {
                    Id = workspace.WorkspaceId.ToString(),
                    Name = workspace.Name,
                    Accent = workspace.AccentKey,
                    WorkingDirectory = workspace.WorkingDirectory,
                    Slot = workspace.Slot
                })
                .ToList()
        });
    }

    /// The set for a first run (or a file with nothing usable in it): one workspace in slot 1, in the directory
    /// Clavis was launched in. A workspace list that starts empty would leave no chat and no way to type.
    public static WorkspaceSet Default(string workingDirectory)
    {
        var (set, _) = WorkspaceUpdate.Create(
            WorkspaceSet.Empty, "", workingDirectory, AccentPalette.Assign([]));
        return set;
    }

    private sealed class WorkspacesDocument
    {
        public string? Active { get; set; }
        public List<WorkspaceEntry>? Workspaces { get; set; }
    }

    private sealed class WorkspaceEntry
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Accent { get; set; }
        public string? WorkingDirectory { get; set; }
        public int Slot { get; set; }
    }
}
