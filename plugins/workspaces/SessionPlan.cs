using System;
using System.Collections.Generic;

namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// How a workspace should obtain its agent session. Three outcomes, because there are three genuinely different
/// situations and collapsing any two of them loses work: an agent may be running right now, or the conversation
/// may exist only as a transcript, or there may be no conversation at all.
public abstract record SessionIntent;

/// No conversation to reopen: spawn a new agent. The first activation of a new workspace.
public sealed record StartFresh(string WorkingDirectory, string Name) : SessionIntent;

/// The conversation exists but nothing is running it, so it is reopened from its transcript. This is the
/// ordinary case after a launch where the agents were not parked, or where a parked agent has since stopped.
public sealed record ResumeConversation(string WorkingDirectory, string AgentSessionId, string Name)
    : SessionIntent;

/// An agent is running this conversation right now, so it is taken over rather than resumed - the provider
/// refuses to resume a session its agent still holds. Covers both a workspace's own parked agent and a tab for
/// an agent started outside Clavis.
public sealed record TakeOver(string InstanceId) : SessionIntent;

/// Deciding which of the three applies. Pure over the workspace and the latest instance list, so the priority
/// between them is testable without a bus or a provider.
public static class SessionPlan
{
    /// A running agent always wins over a transcript. Resuming a conversation an agent still holds is refused by
    /// the provider outright, and even if it were not, it would fork the conversation and lose whatever the
    /// running agent has done since - so "something is running it" is checked first, every time.
    public static SessionIntent For(Workspace workspace, IReadOnlyList<AgentInstance> instances)
    {
        // A fleet tab *is* a running agent - that is the only reason it exists - so it is always a take-over,
        // and never falls through to starting something fresh.
        if (workspace.IsFleetAgent)
        {
            return new TakeOver(workspace.AgentSessionId);
        }

        if (FleetAgents.ParkedFor(workspace, instances) is { } parked)
        {
            return new TakeOver(parked.InstanceId);
        }

        return workspace.HasConversation
            ? new ResumeConversation(workspace.WorkingDirectory, workspace.AgentSessionId, workspace.Name)
            : new StartFresh(workspace.WorkingDirectory, workspace.Name);
    }
}
