using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// One workspace as this plugin owns it: durable user intent (name, accent, working directory, slot) plus the
/// live facts it derives (its session and that session's activity).
public sealed record Workspace
{
    public Guid WorkspaceId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string AccentKey { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";

    /// A stable address, not a position. Slot 0 means "no slot": a workspace beyond the keyboard-switchable
    /// range, reachable by click and from the overview but with no key hint.
    public int Slot { get; init; }

    /// The live agent session, or Guid.Empty before one has started. Sessions start lazily - on first
    /// activation - so restoring eight workspaces does not spawn eight agent processes at launch.
    public Guid SessionId { get; init; }

    /// The provider's own session id for this workspace's conversation, remembered across launches so the
    /// workspace reopens the conversation it had rather than an empty one. Durable, unlike SessionId, which is
    /// only a correlation id for the current run.
    ///
    /// It is a fallback, not the primary route back: a *parked* agent comes up under a new provider id that
    /// Clavis never observes, so a running agent is found by name instead. This id is what resumes the
    /// conversation when no agent is running at all.
    public string AgentSessionId { get; init; } = "";

    /// This "workspace" is really an agent running outside Clavis, shown as a slotless tab so it is visible and
    /// reachable. It is not persisted and holds no slot; activating it takes it over and turns it into a real
    /// workspace.
    public bool IsFleetAgent { get; init; }

    /// A take-over is in progress and the agent is still finishing its turn. Transient, never persisted: it is
    /// what the waiting screen renders, and a restart has nothing to wait for.
    public bool IsAdopting { get; init; }

    public string Activity { get; init; } = WorkspaceActivity.Idle;
    public string ActivityDetail { get; init; } = "";

    /// When this workspace entered its current activity, so the overview renders elapsed time without polling.
    public DateTimeOffset ActivitySince { get; init; } = DateTimeOffset.UtcNow;

    public bool HasSession => SessionId != Guid.Empty;

    /// Whether this workspace knows of a conversation to reopen.
    public bool HasConversation => !string.IsNullOrWhiteSpace(AgentSessionId);
}

/// The three activity states a workspace can be in, mirroring the session activity it is derived from. String
/// literals so they cross load contexts and round-trip through YAML without enum-identity concerns.
public static class WorkspaceActivity
{
    public const string Idle = "idle";
    public const string Working = "working";
    public const string Waiting = "waiting";
}

/// Every workspace plus which one is active. Pure state: no bus, no I/O, no WPF - so the slot arithmetic and
/// the close/activate rules are unit-testable on their own.
public sealed record WorkspaceSet
{
    /// F1-F11 are the keyboard-switchable slots, so 11 is the cap by construction. A workspace created when
    /// every slot is taken gets slot 0 and is reachable by click or from the overview instead.
    public const int SlotCount = 11;

    public IReadOnlyList<Workspace> Workspaces { get; init; } = [];

    public Guid ActiveWorkspaceId { get; init; }

    public Workspace? Active =>
        Workspaces.FirstOrDefault(workspace => workspace.WorkspaceId == ActiveWorkspaceId);

    public static WorkspaceSet Empty { get; } = new();

    public Workspace? ById(Guid workspaceId) =>
        Workspaces.FirstOrDefault(workspace => workspace.WorkspaceId == workspaceId);

    public Workspace? BySlot(int slot) =>
        slot <= 0 ? null : Workspaces.FirstOrDefault(workspace => workspace.Slot == slot);

    public Workspace? BySession(Guid sessionId) =>
        sessionId == Guid.Empty
            ? null
            : Workspaces.FirstOrDefault(workspace => workspace.SessionId == sessionId);

    /// The lowest slot nobody holds, or 0 when all of them are taken.
    public int LowestFreeSlot()
    {
        for (var slot = 1; slot <= SlotCount; slot++)
        {
            if (BySlot(slot) is null)
            {
                return slot;
            }
        }

        return 0;
    }

    /// Workspaces in slot order, with the slotless ones last in creation order - the order the bar and the
    /// overview render, so a freed slot reads as a gap rather than shuffling its neighbours along.
    public IReadOnlyList<Workspace> InSlotOrder() =>
    [
        .. Workspaces.Where(workspace => workspace.Slot > 0).OrderBy(workspace => workspace.Slot),
        .. Workspaces.Where(workspace => workspace.Slot <= 0)
    ];

    public WorkspaceSet With(Guid workspaceId, Func<Workspace, Workspace> updater)
    {
        if (ById(workspaceId) is null)
        {
            return this;
        }

        return this with
        {
            Workspaces =
            [
                .. Workspaces.Select(workspace =>
                    workspace.WorkspaceId == workspaceId ? updater(workspace) : workspace)
            ]
        };
    }
}
