using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// Pairing agent instances to workspaces, and representing the ones that belong to no workspace yet.
///
/// Deliberately provider-neutral: everything here works off what `AgentInstance` carries, so this plugin never
/// references a provider assembly. It also never sees an interactive session - the bridge only publishes
/// instances that are safe to take over - which is why nothing here re-checks that.
public static class FleetAgents
{
    /// Find the agent a workspace should pick back up.
    ///
    /// Matching is by name and directory rather than by a remembered id, and that is forced rather than chosen:
    /// handing a session back gives the parked agent a *new* id, spawned fire-and-forget, so the id it comes up
    /// under is never observed. The name is the one field Clavis writes and the provider preserves.
    ///
    /// An ambiguous match yields null. Two agents answering to one name in one directory means there is no way to
    /// tell which conversation is the workspace's, and attaching it to the wrong one is worse than attaching it to
    /// neither - the unmatched agents still surface as fleet tabs, so nothing is hidden, it is just not guessed.
    public static AgentInstance? ParkedFor(Workspace workspace, IReadOnlyList<AgentInstance> instances)
    {
        if (string.IsNullOrWhiteSpace(workspace.Name))
        {
            return null;
        }

        var matches = instances
            .Where(instance =>
                instance.IsOwned
                && NameMatches(instance.Name, workspace.Name)
                && SameDirectory(instance.WorkingDirectory, workspace.WorkingDirectory))
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    /// The agents no workspace claims, in a stable order. These become the slotless fleet tabs: work that exists
    /// and is reachable, but is not (yet) one of your workspaces.
    ///
    /// An already-adopted instance is excluded - it is somebody's live session, not something to offer again.
    public static IReadOnlyList<AgentInstance> Unclaimed(
        IReadOnlyList<Workspace> workspaces, IReadOnlyList<AgentInstance> instances)
    {
        var claimed = new HashSet<string>(
            workspaces
                .Where(workspace => !workspace.IsFleetAgent)
                .Select(workspace => ParkedFor(workspace, instances)?.InstanceId)
                .Where(instanceId => instanceId is not null)
                .Select(instanceId => instanceId!),
            StringComparer.OrdinalIgnoreCase);

        return
        [
            .. instances
                .Where(instance => !instance.IsAdopted && !claimed.Contains(instance.InstanceId))
                .OrderBy(instance => instance.StartedAt)
        ];
    }

    /// A fleet tab's workspace id, derived from the instance id so it is the same on every discovery pass.
    ///
    /// It has to be stable: the tab is rebuilt from scratch each pass, and a fresh Guid each time would make the
    /// active tab change identity under the user mid-click, and would defeat comparing the set to decide whether
    /// anything actually changed. Derived rather than random for the same reason - there is nowhere to persist it,
    /// because a fleet tab is deliberately not persisted.
    public static Guid SyntheticWorkspaceId(string instanceId)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"clavis-fleet-agent:{instanceId}"));
        return new Guid(hash);
    }

    /// Represent an unclaimed agent as a workspace-shaped tab: slotless (it holds no F-key, because it is not
    /// yours until you take it over) and never persisted.
    public static Workspace AsTab(AgentInstance instance) =>
        new()
        {
            WorkspaceId = SyntheticWorkspaceId(instance.InstanceId),
            Name = instance.Name,
            AccentKey = "",
            WorkingDirectory = instance.WorkingDirectory,
            Slot = 0,
            AgentSessionId = instance.InstanceId,
            IsFleetAgent = true,
            Activity = ActivityOf(instance),
            ActivitySince = instance.StartedAt
        };

    /// A fleet agent's activity, so its tab's dot reads like every other tab's. Only "working" is asserted
    /// positively; anything else reads as idle rather than inventing a state from an unreported status.
    public static string ActivityOf(AgentInstance instance) =>
        string.Equals(instance.Status, "busy", StringComparison.OrdinalIgnoreCase)
            ? WorkspaceActivity.Working
            : WorkspaceActivity.Idle;

    private static bool NameMatches(string instanceName, string workspaceName) =>
        string.Equals(instanceName?.Trim() ?? "", workspaceName.Trim(), StringComparison.OrdinalIgnoreCase);

    /// The provider echoes a directory back as it was given, so one side may carry a trailing separator.
    private static bool SameDirectory(string left, string right) =>
        string.Equals(
            (left ?? "").TrimEnd('\\', '/'),
            (right ?? "").TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
}
