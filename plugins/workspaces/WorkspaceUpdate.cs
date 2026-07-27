using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// Something the shell must do as a consequence of a pure decision. Deliberately not bus messages: the pure
/// update names intent, the plugin translates it, so the rules stay testable without a bus.
public abstract record WorkspaceEffect;

/// Start this workspace's agent session. Emitted on first activation, never at creation, which is what makes
/// "restore eight workspaces without spawning eight agents" true.
public sealed record StartSessionEffect(Guid WorkspaceId, string WorkingDirectory) : WorkspaceEffect;

public sealed record DisposeSessionEffect(Guid SessionId) : WorkspaceEffect;

/// The active workspace changed, so consumers retarget (the layout swaps surfaces, the chat switches, the
/// pickers re-point). Carries the session so a consumer does not have to look it up.
public sealed record ActivatedEffect(Guid WorkspaceId, Guid SessionId) : WorkspaceEffect;

public sealed record ClosedEffect(Guid WorkspaceId) : WorkspaceEffect;

public sealed record SessionStartedEffect(Guid WorkspaceId, Guid SessionId, string WorkingDirectory)
    : WorkspaceEffect;

/// The pure operations over a WorkspaceSet. Every one returns the new set plus what the shell must do, so the
/// slot rules, the lazy session start and the close-and-reactivate behaviour are all testable without a bus.
public static class WorkspaceUpdate
{
    private static readonly WorkspaceEffect[] NoEffects = [];

    /// Create a workspace and activate it. Creating never starts a session; activating does, so a
    /// create-then-activate lands one StartSessionEffect rather than two.
    ///
    /// `slot` places it explicitly (the activate-or-create path, where pressing F5 must give you slot 5);
    /// 0 means "the lowest free slot", which is what a plain create wants. Either way an occupied or
    /// out-of-range slot falls back to the lowest free one, so a workspace never silently steals a key.
    public static (WorkspaceSet Set, WorkspaceEffect[] Effects) Create(
        WorkspaceSet set, string name, string workingDirectory, string accentKey, int slot = 0)
    {
        var placed = slot is >= 1 and <= WorkspaceSet.SlotCount && set.BySlot(slot) is null
            ? slot
            : set.LowestFreeSlot();

        var created = new Workspace
        {
            Name = string.IsNullOrWhiteSpace(name) ? GeneratedName(set, placed) : name.Trim(),
            AccentKey = accentKey,
            WorkingDirectory = workingDirectory,
            Slot = placed
        };

        var added = set with { Workspaces = [.. set.Workspaces, created] };
        return Activate(added, created.WorkspaceId);
    }

    /// Activate a workspace, starting its session the first time. Re-activating the one already active is a
    /// no-op, so a repeated F-key press does not re-announce or re-start anything.
    public static (WorkspaceSet Set, WorkspaceEffect[] Effects) Activate(WorkspaceSet set, Guid workspaceId)
    {
        if (set.ById(workspaceId) is not { } target)
        {
            return (set, NoEffects);
        }

        if (set.ActiveWorkspaceId == workspaceId && target.HasSession)
        {
            return (set, NoEffects);
        }

        var activated = set with { ActiveWorkspaceId = workspaceId };
        var effects = new List<WorkspaceEffect> { new ActivatedEffect(workspaceId, target.SessionId) };
        if (!target.HasSession)
        {
            effects.Add(new StartSessionEffect(workspaceId, target.WorkingDirectory));
        }

        return (activated, effects.ToArray());
    }

    /// Activate the workspace in a slot, or create it there when the slot is free - pressing an unused F-key
    /// is the primary way a workspace comes into being. A slot outside the keyboard-switchable range is
    /// ignored rather than silently landing somewhere else.
    public static (WorkspaceSet Set, WorkspaceEffect[] Effects) ActivateSlot(
        WorkspaceSet set, int slot, string defaultWorkingDirectory, string accentKey)
    {
        if (slot is < 1 or > WorkspaceSet.SlotCount)
        {
            return (set, NoEffects);
        }

        return set.BySlot(slot) is { } occupant
            ? Activate(set, occupant.WorkspaceId)
            : Create(set, "", defaultWorkingDirectory, accentKey, slot);
    }

    /// Close a workspace: dispose its session, drop it, and **free its slot without renumbering anything** -
    /// slot 3 stays slot 3 forever, so a shortcut never moves under you and the bar shows a gap. Closing the
    /// active one activates a neighbour; closing the last one leaves an empty set, which is a legitimate state
    /// (the bar is still the way back in) rather than a refusal.
    public static (WorkspaceSet Set, WorkspaceEffect[] Effects) Close(WorkspaceSet set, Guid workspaceId)
    {
        if (set.ById(workspaceId) is not { } target)
        {
            return (set, NoEffects);
        }

        var remaining = set.Workspaces.Where(workspace => workspace.WorkspaceId != workspaceId).ToList();
        var effects = new List<WorkspaceEffect>();
        if (target.HasSession)
        {
            effects.Add(new DisposeSessionEffect(target.SessionId));
        }

        effects.Add(new ClosedEffect(workspaceId));

        var closed = set with { Workspaces = remaining };
        if (set.ActiveWorkspaceId != workspaceId)
        {
            return (closed, effects.ToArray());
        }

        var neighbour = closed.InSlotOrder().FirstOrDefault();
        if (neighbour is null)
        {
            return (closed with { ActiveWorkspaceId = Guid.Empty }, effects.ToArray());
        }

        var (activated, activationEffects) = Activate(
            closed with { ActiveWorkspaceId = Guid.Empty }, neighbour.WorkspaceId);
        return (activated, [.. effects, .. activationEffects]);
    }

    public static (WorkspaceSet Set, WorkspaceEffect[] Effects) Rename(
        WorkspaceSet set, Guid workspaceId, string name) =>
        string.IsNullOrWhiteSpace(name)
            ? (set, NoEffects)
            : (set.With(workspaceId, workspace => workspace with { Name = name.Trim() }), NoEffects);

    /// Record the session a workspace's lazy start produced.
    public static (WorkspaceSet Set, WorkspaceEffect[] Effects) SessionStarted(
        WorkspaceSet set, Guid workspaceId, Guid sessionId)
    {
        if (set.ById(workspaceId) is not { } target)
        {
            return (set, NoEffects);
        }

        return (
            set.With(workspaceId, workspace => workspace with { SessionId = sessionId }),
            [new SessionStartedEffect(workspaceId, sessionId, target.WorkingDirectory)]);
    }

    /// Fold a session's reported activity onto the workspace that owns it. A session no workspace owns is
    /// ignored: another plugin's session, or one from a workspace already closed.
    public static (WorkspaceSet Set, WorkspaceEffect[] Effects) ApplyActivity(
        WorkspaceSet set, Guid sessionId, string activity, string detail)
    {
        if (set.BySession(sessionId) is not { } owner)
        {
            return (set, NoEffects);
        }

        if (owner.Activity == activity && owner.ActivityDetail == detail)
        {
            return (set, NoEffects);
        }

        return (
            set.With(owner.WorkspaceId, workspace => workspace with { Activity = activity, ActivityDetail = detail }),
            NoEffects);
    }

    // A workspace with no given name is named after the slot it takes, which reads better in the bar than a
    // bare number and is trivially renameable. A slotless one falls back to its position in the list.
    private static string GeneratedName(WorkspaceSet set, int slot) =>
        slot > 0 ? $"Workspace {slot}" : $"Workspace {set.Workspaces.Count + 1}";
}
