using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.TaskTracker;

/// One background task in the tracker. Running while IsDone is false; once the notification lands it flips
/// to done and carries the terminal Status and one-line Summary. TaskId is the provider's correlation id.
public sealed record TaskEntry(string TaskId, string Description, string TaskType, bool IsDone, string Status, string Summary);

/// The pure task-list transitions. Every function takes the current entry list and returns a new one -
/// no mutation, no side effects - so the impure plugin/view shell stays trivially correct and the logic
/// is fully unit-tested. Order is preserved (arrival order) so the list reads top-to-bottom as it filled.
public static class TaskTrackerModel
{
    /// A task began. A first sighting appends a running entry; a repeat of the same id resets that entry
    /// to running (a re-run reuses the slot rather than duplicating it).
    public static IReadOnlyList<TaskEntry> Started(
        IReadOnlyList<TaskEntry> tasks, string taskId, string description, string taskType)
    {
        var running = new TaskEntry(taskId, description, taskType, false, "", "");
        var next = tasks.ToList();
        var index = next.FindIndex(task => task.TaskId == taskId);
        if (index >= 0)
        {
            next[index] = running;
        }
        else
        {
            next.Add(running);
        }

        return next;
    }

    /// A task finished. The matching entry (by id) flips to done, keeping its description and gaining the
    /// terminal status and summary. A notification with no prior start still surfaces as a done entry, so
    /// a missed start never swallows the result.
    public static IReadOnlyList<TaskEntry> Completed(
        IReadOnlyList<TaskEntry> tasks, string taskId, string status, string summary)
    {
        var next = tasks.ToList();
        var index = next.FindIndex(task => task.TaskId == taskId);
        if (index >= 0)
        {
            next[index] = next[index] with { IsDone = true, Status = status, Summary = summary };
        }
        else
        {
            next.Add(new TaskEntry(taskId, summary, "", true, status, summary));
        }

        return next;
    }

    /// Drop a task (used after a completed entry has lingered long enough to read).
    public static IReadOnlyList<TaskEntry> Remove(IReadOnlyList<TaskEntry> tasks, string taskId) =>
        tasks.Where(task => task.TaskId != taskId).ToList();

    /// The number still running, for callers that want the active-only signal.
    public static int RunningCount(IReadOnlyList<TaskEntry> tasks) => tasks.Count(task => !task.IsDone);
}
