using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Workspaces.Views;

/// The strip the Workspaces plugin contributes into the host's `workspace-bar` region: one tab per workspace,
/// plus a create and a quit affordance at the right.
///
/// Three colour languages, kept strictly apart, because mixing them is how a status strip becomes unreadable:
/// the **number** is position (blue), the **dot** is state (grey idle / green working / yellow waiting), and the
/// **2px tick** is identity (the workspace accent). Only the dot ever moves, and only for work in progress -
/// "waiting" is the more urgent state and draws the eye by colour rather than by pulsing, so it stays legible
/// next to a breathing neighbour.
[ExcludeFromCodeCoverage] // WPF composition; the projection and the activity mapping are WorkspaceBarRows
public static class WorkspaceBarView
{
    // Sized against the host's 60px strip. The type and the marks scale by roughly half again rather than
    // doubling with the height: at double size the numbers start competing with the workspace names, and the
    // strip is meant to be read at a glance, not stared at.
    private const double TabWidth = 220;
    private const double LabelSize = 13;
    private const double GlyphSize = 24;

    public static FrameworkElement Create(IBus bus)
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };

        // A word and a glyph do not read at the same size: "QUIT" is set at label size, "+" at glyph size, so
        // both land at about the same visual weight on the strip.
        var quit = TailButton("QUIT", "Quit Clavis", LabelSize, isDestructive: true, () => bus.Send(new ExitApplication()));
        var create = TailButton("+", "New workspace", GlyphSize, isDestructive: false, () => bus.Send(new CreateWorkspace("", "")));

        var tail = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tail.Children.Add(create);
        tail.Children.Add(quit);
        DockPanel.SetDock(tail, Dock.Right);

        var layout = new DockPanel { LastChildFill = true };
        layout.Children.Add(tail);
        layout.Children.Add(tabs);

        ISubscription? subscription = null;

        void Render(WorkspaceListChanged message)
        {
            var rows = WorkspaceBarRows.Build([.. Project(message.Workspaces)], message.ActiveWorkspaceId);
            tabs.Children.Clear();
            foreach (var row in rows)
            {
                tabs.Children.Add(BuildTab(bus, row));
            }
        }

        layout.Loaded += (_, _) =>
        {
            subscription ??= bus.Subscribe<WorkspaceListChanged>(message =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() => Render(message));
                return Task.CompletedTask;
            });

            bus.Send(new RequestWorkspaces());
        };

        layout.Unloaded += (_, _) =>
        {
            subscription?.Dispose();
            subscription = null;
        };

        return layout;
    }

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
                Slot = workspace.Slot,
                IsFleetAgent = workspace.IsFleetAgent,
                IsAdopting = workspace.IsAdopting
            };
        }
    }

    private static FrameworkElement BuildTab(IBus bus, WorkspaceBarRow row)
    {
        // Identity: a 2px tick of constant length. Its geometry never changes with selection - a workspace does
        // not become more or less itself because you are looking at it - only its opacity lifts.
        //
        // A fleet agent has no identity here yet: it is somebody's running work, not one of your places, so it
        // gets no accent. That absence is the signal - the tick is what says "this is a place of yours".
        var tick = new Border { Width = 3, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 12, 13, 12) };
        tick.SetResourceReference(Border.BackgroundProperty, row.IsFleetAgent ? "TextDimBrush" : row.AccentKey);
        tick.Opacity = row.IsFleetAgent ? 0.3 : row.IsActive ? 1.0 : 0.55;

        // Position: the slot number, in the primary blue. A fleet agent holds no slot, so it shows a mark in that
        // column instead of a number - the column stays aligned, and the missing number is the point.
        var number = new TextBlock
        {
            Text = row.IsFleetAgent ? "~" : row.SlotNumber,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Opacity = row.IsActive ? 1.0 : 0.55
        };
        number.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        number.SetResourceReference(
            TextBlock.ForegroundProperty, row.IsFleetAgent ? "TextDimBrush" : "ClavisBrush");

        // State: a circle, never a box.
        var dot = new Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, row.ActivityBrushKey);
        if (row.IsBreathing)
        {
            Motion.breathe(dot);
        }
        else
        {
            dot.Opacity = row.IsActive || row.ActivityBrushKey != "TextDimBrush" ? 1.0 : 0.5;
        }

        var title = new TextBlock
        {
            Text = row.Title,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = row.Tooltip
        };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        title.SetResourceReference(TextBlock.ForegroundProperty, row.IsActive ? "TextBrightBrush" : "TextDimBrush");

        var content = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 12, 0) };
        content.Children.Add(tick);
        content.Children.Add(number);
        content.Children.Add(dot);
        content.Children.Add(title);

        // Square corners. The active tab is marked by a slightly raised fill, not by a border.
        var tab = new Border
        {
            Width = TabWidth,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = content
        };
        tab.SetResourceReference(
            Border.BackgroundProperty, row.IsActive ? "RaisedBrush" : "FaintBrush");

        // Clicking a tab both activates the workspace and summons Clavis - the bar stays visible when everything
        // else is hidden, so a click there is often how you come back.
        tab.MouseLeftButtonUp += (_, _) =>
        {
            bus.Send(new ActivateWorkspace(row.WorkspaceId));
            bus.Send(new SummonClavis());
        };

        return tab;
    }

    // Quit is the only destructive gesture on the strip, so it is the only one that turns red on hover.
    private static FrameworkElement TailButton(
        string glyph, string tooltip, double fontSize, bool isDestructive, Action onClick)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontSize = fontSize,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = tooltip
        };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var button = new Border
        {
            Width = 60,
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = text
        };

        button.MouseEnter += (_, _) =>
            text.SetResourceReference(
                TextBlock.ForegroundProperty, isDestructive ? "ErrorBrush" : "TextBrightBrush");
        button.MouseLeave += (_, _) => text.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        button.MouseLeftButtonUp += (_, _) => onClick();
        return button;
    }
}
