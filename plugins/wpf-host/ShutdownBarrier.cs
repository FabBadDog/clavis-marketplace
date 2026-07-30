using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Who still has to answer before the application may exit.
///
/// Pure and separate from the window manager so the one property that matters is testable without a WPF
/// application: the barrier always opens. A plugin that declares itself a participant and then never answers -
/// crashed, wedged, or waiting on something that will never arrive - must not be able to make Clavis
/// unquittable, so "everyone answered" and "we waited long enough" are both ways through.
public sealed class ShutdownBarrier
{
    private readonly HashSet<string> _participants = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ready = new(StringComparer.OrdinalIgnoreCase);

    /// True once quitting has started, so the gesture is idempotent: closing the window while a quit is already
    /// under way must not restart the barrier or send a second broadcast.
    public bool IsPreparing { get; private set; }

    /// True once the application has been told to exit, so it is only ever told once - a second
    /// ApplicationShutdown after the dispatcher has begun shutting down is at best noise.
    public bool HasExited { get; private set; }

    public void Declare(string pluginId)
    {
        if (!string.IsNullOrWhiteSpace(pluginId))
        {
            _participants.Add(pluginId.Trim());
        }
    }

    /// Begin quitting. False means a quit was already under way and this gesture should do nothing further.
    public bool BeginPreparing()
    {
        if (IsPreparing)
        {
            return false;
        }

        IsPreparing = true;
        return true;
    }

    /// Record an answer. True means this was the last one outstanding and the application may now exit.
    public bool Ready(string pluginId)
    {
        if (!string.IsNullOrWhiteSpace(pluginId))
        {
            _ready.Add(pluginId.Trim());
        }

        return IsSatisfied;
    }

    /// Whether nobody is left to wait for. Also true when nothing ever declared itself, which is the common case
    /// and must exit immediately rather than sit through the grace period for no reason.
    public bool IsSatisfied => _participants.All(_ready.Contains);

    /// Claim the single right to tell the application to exit. False when that has already happened.
    public bool TryExit()
    {
        if (HasExited)
        {
            return false;
        }

        HasExited = true;
        return true;
    }

    /// Who has not answered, for the log line when the grace period runs out. Naming them is the difference
    /// between a diagnosable "workspaces never finished parking" and an unexplained pause on every quit.
    public IReadOnlyList<string> Outstanding =>
        [.. _participants.Where(participant => !_ready.Contains(participant)).OrderBy(participant => participant)];
}
