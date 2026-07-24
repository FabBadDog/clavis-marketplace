using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.TaskTracker;

/// Subscribes to the neutral task stream and drives the status-bar tracker. The task-list logic is the
/// pure TaskTrackerModel; this shell only marshals bus events onto the UI thread, applies the model, and
/// schedules the linger-then-remove of finished tasks. All list mutation happens on the dispatcher, so no
/// lock is needed.
[ExcludeFromCodeCoverage] // impure WPF + bus wiring; the transitions are covered via TaskTrackerModel
public sealed class TaskTrackerPlugin : IPlugin<TaskTrackerConfig>
{
    // How long a finished task stays on screen so its result is readable before it fades out.
    private static readonly TimeSpan CompletedLinger = TimeSpan.FromSeconds(8);

    private IReadOnlyList<TaskEntry> _tasks = Array.Empty<TaskEntry>();
    private TaskTrackerView? _view;

    public string Id => "TaskTracker";

    public TaskTrackerConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(TaskTrackerConfig config) =>
        Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, TaskTrackerConfig config)
    {
        if (Application.Current is not null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _view = new TaskTrackerView();
                _view.Apply(_tasks);

                bus.Send(new UiRegionContribution(
                    "status-bar-right", "TaskTracker", 0, () => _view.Element));
            });
        }

        var subscription = bus.Subscribe<AgentStreamEvent>(evt =>
        {
            switch (evt)
            {
                case AgentTaskStarted started:
                    OnUi(() => Apply(TaskTrackerModel.Started(
                        _tasks, started.TaskId, started.Description, started.TaskType)));
                    break;
                case AgentTaskCompleted completed:
                    OnUi(() =>
                    {
                        Apply(TaskTrackerModel.Completed(
                            _tasks, completed.TaskId, completed.Status, completed.Summary));
                        ScheduleRemoval(completed.TaskId);
                    });
                    break;
            }

            return Task.CompletedTask;
        });

        bus.LogInfo(Id, "Task tracker plugin activated");

        return Task.FromResult<IDisposable>(new PluginDisposable(subscription));
    }

    private void Apply(IReadOnlyList<TaskEntry> tasks)
    {
        _tasks = tasks;
        _view?.Apply(_tasks);
    }

    // After the linger, drop the finished task and re-render (the strip collapses once the last clears).
    private void ScheduleRemoval(string taskId)
    {
        var timer = new DispatcherTimer { Interval = CompletedLinger };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Apply(TaskTrackerModel.Remove(_tasks, taskId));
        };
        timer.Start();
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.InvokeAsync(action);
    }

    private sealed class PluginDisposable(ISubscription subscription) : IDisposable
    {
        public void Dispose() => subscription.Dispose();
    }
}
