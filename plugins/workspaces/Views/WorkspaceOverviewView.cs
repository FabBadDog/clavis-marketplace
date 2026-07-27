using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Workspaces.Views;

/// The F12 overview: every workspace with its slot key, accent, working directory and what it is doing. An
/// ordinary panel kind, so it inherits open/toggle/close/restore/persist/tear-off/Esc and a palette command for
/// free - a bespoke chromeless overlay would have been the third overlay mechanism in the application.
///
/// Rows are rebuilt from each `WorkspaceListChanged`; the list is small and every change redraws it, which is
/// simpler than reconciling and cheap at this size. Clicking a row activates that workspace.
[ExcludeFromCodeCoverage] // WPF composition; the row projection is WorkspaceOverviewRows
public static class WorkspaceOverviewView
{
    public static FrameworkElement Create(IBus bus)
    {
        var rows = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = rows,
            Focusable = false
        };
        scroller.SetResourceReference(Control.BackgroundProperty, "BlackBrush");

        ISubscription? subscription = null;

        void Render(WorkspaceListChanged message)
        {
            var built = WorkspaceOverviewRows.Build(
                [.. Project(message.Workspaces)], message.ActiveWorkspaceId, DateTimeOffset.UtcNow);

            rows.Children.Clear();
            foreach (var row in built)
            {
                rows.Children.Add(BuildRow(bus, row));
            }
        }

        scroller.Loaded += (_, _) =>
        {
            subscription ??= bus.Subscribe<WorkspaceListChanged>(message =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() => Render(message));
                return Task.CompletedTask;
            });

            // The panel can open long after the last list change, so ask for the current one rather than
            // waiting for something to move.
            bus.Send(new RequestWorkspaces());
        };

        scroller.Unloaded += (_, _) =>
        {
            subscription?.Dispose();
            subscription = null;
        };

        return scroller;
    }

    // The contract carries WorkspaceInfo; the row projection is written against the plugin's own Workspace so it
    // stays usable from the pure side. This maps one to the other rather than duplicating the projection.
    private static IEnumerable<Workspace> Project(IReadOnlyList<WorkspaceInfo> workspaces)
    {
        foreach (var workspace in workspaces)
        {
            yield return new Workspace
            {
                WorkspaceId = workspace.WorkspaceId,
                Name = workspace.Name,
                AccentKey = workspace.AccentKey,
                WorkingDirectory = workspace.WorkingDirectory,
                SessionId = workspace.SessionId,
                Activity = workspace.Activity,
                ActivityDetail = workspace.ActivityDetail,
                ActivitySince = workspace.ActivitySince,
                Slot = workspace.Slot
            };
        }
    }

    private static FrameworkElement BuildRow(IBus bus, WorkspaceOverviewRow row)
    {
        // The identity accent is a 2px tick of constant length - never a tinted area, and never the dot, which
        // carries activity instead. Three separate colour languages: number = position, dot = state, tick =
        // identity.
        var tick = new Border { Width = 2, Height = 26, Margin = new Thickness(0, 0, 12, 0) };
        tick.SetResourceReference(Border.BackgroundProperty, row.AccentKey);
        tick.Opacity = row.IsActive ? 1.0 : 0.55;

        var slot = new TextBlock
        {
            Text = row.SlotLabel,
            Width = 34,
            FontSize = 12,
            Opacity = row.IsActive ? 1.0 : 0.55,
            VerticalAlignment = VerticalAlignment.Center
        };
        slot.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        slot.SetResourceReference(TextBlock.ForegroundProperty, "ClavisBrush");

        // An activity indicator is a circle, never a box.
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        dot.SetResourceReference(Shape.FillProperty, ActivityBrushKey(row.Activity));
        dot.Opacity = row.Activity == WorkspaceActivity.Idle ? 0.45 : 1.0;

        var name = new TextBlock
        {
            Text = row.Name,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 150
        };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        name.SetResourceReference(
            TextBlock.ForegroundProperty, row.IsActive ? "TextBrightBrush" : "TextDimBrush");

        var directory = new TextBlock
        {
            Text = row.WorkingDirectory,
            FontSize = 10,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = row.WorkingDirectory
        };
        directory.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        directory.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var status = new TextBlock
        {
            Text = StatusText(row),
            FontSize = 10,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        status.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        DockPanel.SetDock(status, Dock.Right);

        var line = new DockPanel { LastChildFill = true };
        line.Children.Add(status);
        line.Children.Add(tick);
        line.Children.Add(slot);
        line.Children.Add(dot);
        line.Children.Add(name);
        line.Children.Add(directory);

        // Square corners, no frame: the row is separated by whitespace, not by a box.
        var container = new Border
        {
            Padding = new Thickness(0, 7, 0, 7),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = line
        };
        container.MouseLeftButtonUp += (_, _) => bus.Send(new ActivateWorkspace(row.WorkspaceId));
        return container;
    }

    // The dot carries activity by colour, and only "working" is allowed to be the pulsing one elsewhere - here
    // it is steady, since a list of pulsing dots would be noise rather than signal.
    private static string ActivityBrushKey(string activity) => activity switch
    {
        WorkspaceActivity.Working => "GreenBrush",
        WorkspaceActivity.Waiting => "WarnBrush",
        _ => "TextDimBrush"
    };

    private static string StatusText(WorkspaceOverviewRow row)
    {
        if (!row.HasSession)
        {
            return "not started";
        }

        var detail = string.IsNullOrWhiteSpace(row.Detail) ? row.Activity : $"{row.Activity} · {row.Detail}";
        return string.IsNullOrEmpty(row.Elapsed) ? detail : $"{detail} · {row.Elapsed}";
    }
}
