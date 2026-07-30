namespace FabioSoft.Contracts.Workspace

open System
open System.Collections.Generic
open System.ComponentModel

/// One workspace as the rest of the application sees it: an identity, a name, an accent, a working directory,
/// its live agent session, its derived activity, and the slot it answers to.
///
/// Slot is a stable address, not a position: closing a workspace frees its slot and renumbers nothing, so the
/// key you learned never moves under you. AccentKey is a theme *resource key* (never a baked colour), so a
/// randomly assigned accent survives a restart and stays re-themable.
[<Sealed>]
type WorkspaceInfo
    (workspaceId: Guid,
     name: string,
     accentKey: string,
     workingDirectory: string,
     sessionId: Guid,
     activity: string,
     activityDetail: string,
     activitySince: DateTimeOffset,
     slot: int,
     isFleetAgent: bool,
     isAdopting: bool) =

    new(workspaceId, name, accentKey, workingDirectory, sessionId, activity, activityDetail, activitySince, slot) =
        WorkspaceInfo(
            workspaceId, name, accentKey, workingDirectory, sessionId, activity, activityDetail, activitySince,
            slot, false, false)

    member _.WorkspaceId = workspaceId
    member _.Name = name
    member _.AccentKey = accentKey
    member _.WorkingDirectory = workingDirectory

    /// The workspace's live agent session, or Guid.Empty before it has started one. Sessions start lazily -
    /// on first activation - so restoring eight workspaces does not spawn eight agent processes at launch.
    member _.SessionId = sessionId

    /// A SessionActivity literal, derived from the session's activity, so a workspace you are not looking at
    /// can still say it needs you.
    member _.Activity = activity
    member _.ActivityDetail = activityDetail

    /// When the workspace entered its current activity, so a consumer renders elapsed time without polling.
    member _.ActivitySince = activitySince

    member _.Slot = slot

    /// This entry is not a workspace of yours but an agent running outside Clavis, surfaced so it is visible and
    /// reachable. It holds no slot and is not persisted; activating it takes the agent over, and it becomes an
    /// ordinary workspace at that point. A consumer should render it as distinct from the real ones.
    member _.IsFleetAgent = isFleetAgent

    /// A take-over is in progress and the agent has not let go yet - it is finishing the turn it was mid-way
    /// through, because stopping it earlier would discard that work. There is no session to show until it does,
    /// so a consumer showing conversations should say what is being waited for rather than show an empty one.
    member _.IsAdopting = isAdopting

/// The whole workspace list, broadcast whenever anything in it changes (created, closed, renamed, activated,
/// activity moved). One message rather than a set of deltas: the list is small, consumers render all of it,
/// and a single snapshot cannot leave a subscriber half-updated.
[<Sealed>]
type WorkspaceListChanged(workspaces: IReadOnlyList<WorkspaceInfo>, activeWorkspaceId: Guid) =
    member _.Workspaces = workspaces
    member _.ActiveWorkspaceId = activeWorkspaceId

/// A late subscriber asks for the current list, so activation order does not matter (mirrors
/// PanelKindsRequested).
[<Sealed>]
type RequestWorkspaces() =
    do ()

[<Sealed>]
[<Description("Activate a workspace by id")>]
type ActivateWorkspace(workspaceId: Guid) =
    member _.WorkspaceId = workspaceId

/// Activate the workspace in a slot - or CREATE it there if the slot is free. Pressing an unused F-key is the
/// primary way a workspace comes into being, so this is activate-or-create, never a no-op. A slot outside the
/// keyboard-switchable range is ignored.
[<Sealed>]
[<Description("Activate or create the workspace in a slot (1-11)")>]
type ActivateWorkspaceSlot(slot: int) =
    member _.Slot = slot

/// A workspace became the active one, carrying its live session so a consumer can retarget without looking
/// the workspace up. SessionId is Guid.Empty when the session has not started yet; WorkspaceSessionStarted
/// follows once it has.
[<Sealed>]
type WorkspaceActivated(workspaceId: Guid, sessionId: Guid) =
    member _.WorkspaceId = workspaceId
    member _.SessionId = sessionId

/// A workspace's agent session has been started (lazily, on first activation) in the given directory.
[<Sealed>]
type WorkspaceSessionStarted(workspaceId: Guid, sessionId: Guid, workingDirectory: string) =
    member _.WorkspaceId = workspaceId
    member _.SessionId = sessionId
    member _.WorkingDirectory = workingDirectory

/// Create a workspace and activate it. An empty name gets a generated one; an empty working directory falls
/// back to the one the application was launched in.
[<Sealed>]
[<Description("Create a new workspace")>]
type CreateWorkspace(name: string, workingDirectory: string) =
    member _.Name = name
    member _.WorkingDirectory = workingDirectory

/// Close the active workspace: dispose its session, drop its tab, free its slot, carry on. This is what the
/// palette's `exit` now means - quitting the application is ExitApplication.
[<Sealed>]
[<Description("Close the active workspace")>]
type CloseActiveWorkspace() =
    do ()

[<Sealed>]
type CloseWorkspace(workspaceId: Guid) =
    member _.WorkspaceId = workspaceId

[<Sealed>]
[<Description("Rename a workspace")>]
type RenameWorkspace(workspaceId: Guid, name: string) =
    member _.WorkspaceId = workspaceId
    member _.Name = name

[<Sealed>]
type WorkspaceClosed(workspaceId: Guid) =
    member _.WorkspaceId = workspaceId

/// Stop waiting for the agent this workspace is taking over, and take it over now.
///
/// The wait exists because taking an agent over stops it, discarding whatever turn it was mid-way through. That
/// trade is the user's to make, not the application's - which is why waiting is the default and this is a
/// deliberate gesture rather than a timeout.
[<Sealed>]
[<Description("Take over this workspace's agent now, without waiting for its turn to finish")>]
type ForceTakeOver(workspaceId: Guid) =
    member _.WorkspaceId = workspaceId
