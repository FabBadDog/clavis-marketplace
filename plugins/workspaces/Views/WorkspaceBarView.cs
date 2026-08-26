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

        // One live tab per workspace, reused across renders rather than rebuilt - see WorkspaceTab for why that
        // matters for clicking.
        var tabsById = new Dictionary<Guid, WorkspaceTab>();

        void Render(WorkspaceListChanged message)
        {
            var rows = WorkspaceBarRows.Build([.. Project(message.Workspaces)], message.ActiveWorkspaceId);

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (!tabsById.TryGetValue(row.WorkspaceId, out var tab))
                {
                    tab = new WorkspaceTab(bus, row.WorkspaceId);
                    tabsById[row.WorkspaceId] = tab;
                }

                tab.Apply(row);
                Place(tabs, tab.Root, index);
            }

            Prune(tabs, tabsById, rows);
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

    // Move a tab to its place in the strip, and leave it alone when it is already there: reordering by
    // remove-and-insert on every render would defeat the point of keeping the visuals alive.
    private static void Place(Panel strip, FrameworkElement tab, int index)
    {
        var current = strip.Children.IndexOf(tab);
        if (current == index)
        {
            return;
        }

        if (current >= 0)
        {
            strip.Children.RemoveAt(current);
        }

        strip.Children.Insert(index < strip.Children.Count ? index : strip.Children.Count, tab);
    }

    // Drop the visuals of workspaces that are no longer on the strip - a closed workspace, or a fleet agent
    // that stopped being offered.
    private static void Prune(
        Panel strip, Dictionary<Guid, WorkspaceTab> tabs, IReadOnlyList<WorkspaceBarRow> rows)
    {
        var live = new HashSet<Guid>();
        foreach (var row in rows)
        {
            live.Add(row.WorkspaceId);
        }

        var gone = new List<Guid>();
        foreach (var entry in tabs)
        {
            if (!live.Contains(entry.Key))
            {
                gone.Add(entry.Key);
            }
        }

        foreach (var workspaceId in gone)
        {
            strip.Children.Remove(tabs[workspaceId].Root);
            tabs.Remove(workspaceId);
        }
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

    /// One tab's visuals, kept alive from one render to the next.
    ///
    /// The strip re-renders on every workspace-list change, and a session's activity alone changes that list
    /// while an agent works. Rebuilding the tabs each time restarted the breathing animation - and, worse, threw
    /// clicks away: a tab replaced between its mouse-down and the matching mouse-up never sees the up, so the
    /// click that switches workspace silently did nothing. That is why clicking a tab "only worked sometimes",
    /// and why waiting for the agent to fall quiet made it work again.
    [ExcludeFromCodeCoverage] // WPF composition; the projection and the activity mapping are WorkspaceBarRows
    private sealed class WorkspaceTab
    {
        private readonly Border _tick = new()
        {
            Width = 3,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 12, 13, 12)
        };

        private readonly TextBlock _number = new()
        {
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };

        private readonly Ellipse _dot = new()
        {
            Width = 12,
            Height = 12,
            Margin = new Thickness(0, 0, 11, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        private readonly TextBlock _title = new()
        {
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // What this tab already shows, so a render only touches what actually changed. Re-resolving resource
        // references on every activity tick is what made the strip flicker.
        private WorkspaceBarRow? _applied;
        private bool _breathing;

        public WorkspaceTab(IBus bus, Guid workspaceId)
        {
            _number.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
            _title.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");

            var content = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 12, 0) };
            content.Children.Add(_tick);
            content.Children.Add(_number);
            content.Children.Add(_dot);
            content.Children.Add(_title);

            // Square corners. The active tab is marked by a slightly raised fill, not by a border.
            Root = new Border
            {
                Width = TabWidth,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = content
            };

            // Clicking a tab both activates the workspace and summons Clavis - the bar stays visible when
            // everything else is hidden, so a click there is often how you come back.
            Root.MouseLeftButtonUp += (_, _) =>
            {
                bus.Send(new ActivateWorkspace(workspaceId));
                bus.Send(new SummonClavis());
            };
        }

        public Border Root { get; }

        public void Apply(WorkspaceBarRow row)
        {
            var previous = _applied;
            _applied = row;

            // Identity: a 2px tick of constant length. Its geometry never changes with selection - a workspace
            // does not become more or less itself because you are looking at it - only its opacity lifts.
            //
            // A fleet agent has no identity here yet: it is somebody's running work, not one of your places, so
            // it gets no accent. That absence is the signal - the tick is what says "this is a place of yours".
            if (previous is null
                || previous.AccentKey != row.AccentKey
                || previous.IsFleetAgent != row.IsFleetAgent)
            {
                _tick.SetResourceReference(
                    Border.BackgroundProperty, row.IsFleetAgent ? "TextDimBrush" : row.AccentKey);
                _number.SetResourceReference(
                    TextBlock.ForegroundProperty, row.IsFleetAgent ? "TextDimBrush" : "ClavisBrush");
            }

            _tick.Opacity = row.IsFleetAgent ? 0.3 : row.IsActive ? 1.0 : 0.55;

            // Position: the slot number, in the primary blue. A fleet agent holds no slot, so it shows a mark in
            // that column instead of a number - the column stays aligned, and the missing number is the point.
            _number.Text = row.IsFleetAgent ? "~" : row.SlotNumber;
            _number.Opacity = row.IsActive ? 1.0 : 0.55;

            // State: a circle, never a box.
            if (previous is null || previous.ActivityBrushKey != row.ActivityBrushKey)
            {
                _dot.SetResourceReference(Shape.FillProperty, row.ActivityBrushKey);
            }

            ApplyBreathing(row);

            _title.Text = row.Title;
            _title.ToolTip = row.Tooltip;
            if (previous is null || previous.IsActive != row.IsActive)
            {
                _title.SetResourceReference(
                    TextBlock.ForegroundProperty, row.IsActive ? "TextBrightBrush" : "TextDimBrush");
                Root.SetResourceReference(Border.BackgroundProperty, row.IsActive ? "RaisedBrush" : "FaintBrush");
            }
        }

        // Breathing is started once and then left running: restarting it on every activity tick made the dot
        // stutter. Stopping it clears the animation explicitly, because a held animated value outranks a direct
        // write to the same property.
        private void ApplyBreathing(WorkspaceBarRow row)
        {
            if (row.IsBreathing)
            {
                if (_breathing)
                {
                    return;
                }

                _breathing = true;
                Motion.breathe(_dot);
                return;
            }

            if (_breathing)
            {
                _breathing = false;
                _dot.BeginAnimation(UIElement.OpacityProperty, null);
            }

            _dot.Opacity = row.IsActive || row.ActivityBrushKey != "TextDimBrush" ? 1.0 : 0.5;
        }
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
