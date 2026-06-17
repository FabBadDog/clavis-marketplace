using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FabioSoft.Contracts.Placeholders;
using FabioSoft.Contracts.Services;
using FabioSoft.Contracts.Session;
using FabioSoft.Contracts.Workspace;
using FabioSoft.Clavis.Controls;
using FabioSoft.Clavis.Placeholders;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Conversation.Views;

// One editable zone (its label and how to read/write its template on the working set, which the closures
// capture so they always target the live working templates even after a config reload swaps it).
internal sealed record EditorZone(string Label, Func<string> Get, Action<string> Set);

// A placement within a panel's window chrome (Window title / Status bar / Turn status) and its ordered zones.
internal sealed record EditorPlacement(string Name, IReadOnlyList<EditorZone> Zones);

// A configurable panel: the chat, or a registered panel kind, shown by its friendly name.
internal sealed record EditorPanel(string Kind, string Name, bool IsChat);

/// The status-line editor panel: pick a panel, then a placement (the window title bar or status bar - and,
/// for the chat, the turn-status column), edit each of its zones with IntelliSense and a live preview, and
/// Save - persisting to the shared "StatusLine" config the chrome reads. The window owns the title/status
/// bars; the active panel drives their content, so configuring a panel here sets what its bars show while it
/// is active. Self-contained: it drives everything through the bus (config load/save, panel-kind catalog,
/// provider descriptors for IntelliSense, value snapshots for the previews).
internal static class StatusLineEditorView
{
    private const string ChatKind = PanelChromeResolver.ChatKind;

    public static FrameworkElement Create(IBus bus)
    {
        var working = new StatusLineTemplates();
        var descriptorsByProvider = new Dictionary<string, IReadOnlyList<PlaceholderDescriptor>>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var limitWindows = Array.Empty<LimitWindow>();
        var zonePreviews = new List<(EditorZone Zone, PlaceholderStrip Strip)>();
        // The live editors of the visible placement, kept so their text can be flushed into the working
        // templates before any rebuild or save - the source of truth for persistence is the box, not a
        // TextChanged side effect that can be missed.
        var currentEditors = new List<(EditorZone Zone, TextBox Editor)>();
        var suppress = false;
        var rebuilding = false;

        // The chat is always first; registered kinds are appended (by friendly name) as their registrations arrive.
        var panels = new List<EditorPanel> { new(ChatKind, "Chat", true) };

        IReadOnlyList<PlaceholderDescriptor> AllDescriptors()
        {
            var all = new List<PlaceholderDescriptor>();
            foreach (var list in descriptorsByProvider.Values)
            {
                all.AddRange(list);
            }
            return all;
        }

        void RefreshPreviews()
        {
            var snapshot = new Dictionary<string, string>(values);
            foreach (var (_, strip) in zonePreviews)
            {
                strip.SetValues(snapshot);
            }
        }

        void RefreshLimitWindows()
        {
            foreach (var (_, strip) in zonePreviews)
            {
                strip.SetLimitWindows(limitWindows);
            }
        }

        // Write the visible editors' text back into the working templates. Done before every rebuild and the
        // save, so a switch of placement/panel never drops what was typed and the save persists exactly what
        // is on screen, independent of TextChanged timing.
        void FlushEditors()
        {
            foreach (var (zone, editor) in currentEditors)
            {
                zone.Set(editor.Text);
            }
        }

        // The per-kind override, created on demand only when written (so merely viewing a panel never adds an
        // empty entry to the saved config).
        PanelChrome ChromeFor(string kind)
        {
            if (!working.Panels.TryGetValue(kind, out var chrome))
            {
                chrome = new PanelChrome();
                working.Panels[kind] = chrome;
            }
            return chrome;
        }

        string ChromeGet(string kind, Func<PanelChrome, string> get) =>
            working.Panels.TryGetValue(kind, out var chrome) ? get(chrome) : "";

        var panelCombo = Inputs.combo();
        var placementCombo = Inputs.combo();
        var zonesHost = new StackPanel();

        void AttachCompletion(TextBox editor)
        {
            // The popup's child is not in the window's visual tree, so DynamicResource references can fall
            // back to defaults (the cause of the tiny, undefined-looking text). Resolve the theme tokens off
            // the connected editor once and apply them as literals, so the list reads at the design's body
            // role - the legible agent (Inter) face at the body size, not a system fallback.
            var list = new ListBox
            {
                MaxHeight = 240,
                BorderThickness = new Thickness(0),
                FontFamily = ResolveFontFamily(editor, "AgentFont"),
                FontSize = ResolveDouble(editor, "BodyFontSize", 14.0),
                Background = ResolveBrush(editor, "BlackBrush", Brushes.Black),
                Foreground = ResolveBrush(editor, "TextBrush", Brushes.White)
            };
            var popup = new Popup
            {
                PlacementTarget = editor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                Child = new Border
                {
                    Child = list,
                    MinWidth = 320,
                    BorderThickness = new Thickness(1),
                    Background = ResolveBrush(editor, "BlackBrush", Brushes.Black),
                    BorderBrush = ResolveBrush(editor, "FrameBrush", Brushes.Gray)
                }
            };

            void Close() => popup.IsOpen = false;

            void Show()
            {
                var result = PlaceholderCompletion.Complete(
                    editor.Text, editor.CaretIndex, AllDescriptors(), PlaceholderComponents.All, PlaceholderFormats.Known);
                if (result.Items.Count == 0)
                {
                    Close();
                    return;
                }
                list.Items.Clear();
                foreach (var item in result.Items)
                {
                    list.Items.Add(new CompletionRow(item, result.ReplaceStart));
                }
                list.SelectedIndex = 0;
                popup.IsOpen = true;
            }

            void Accept()
            {
                if (list.SelectedItem is not CompletionRow row)
                {
                    return;
                }
                var caret = editor.CaretIndex;
                Close();
                // Plain assignment: the natural TextChanged updates the working template and preview.
                // (Never re-raise TextChanged with a bare RoutedEventArgs - a TextChangedEventHandler
                // rejects it with an ArgumentException at dispatch.)
                editor.Text = editor.Text[..row.ReplaceStart] + row.Item.InsertText + editor.Text[caret..];
                editor.CaretIndex = row.ReplaceStart + row.Item.InsertText.Length;
                // Chained completion: accepting a namespace ("agent.") immediately offers its keys.
                Show();
            }

            editor.PreviewKeyDown += (_, args) =>
            {
                if (!popup.IsOpen)
                {
                    return;
                }
                switch (args.Key)
                {
                    case Key.Down:
                        list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1);
                        args.Handled = true;
                        break;
                    case Key.Up:
                        list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0);
                        args.Handled = true;
                        break;
                    case Key.Enter:
                    case Key.Tab:
                        Accept();
                        args.Handled = true;
                        break;
                    case Key.Escape:
                        Close();
                        args.Handled = true;
                        break;
                }
            };
            list.MouseDoubleClick += (_, _) => Accept();
            editor.GotKeyboardFocus += (_, _) => Show();
            // Recompute while typing - completion only on focus meant typing '{' never opened the popup.
            editor.TextChanged += (_, _) =>
            {
                if (!suppress)
                {
                    Show();
                }
            };
            // A caret move re-filters an open popup (and closes it when the caret leaves the placeholder).
            editor.SelectionChanged += (_, _) =>
            {
                if (popup.IsOpen)
                {
                    Show();
                }
            };
        }

        // The placement preview reuses the real chrome controls so it behaves exactly like the live bar: the
        // status bar (three zones) is the same ResponsiveZoneBar that shrinks and drops zones as it narrows;
        // the title (two zones) is the same left-docked / right-docked composition. Live values and usage
        // windows are pushed in; unlike the live bar, unresolved tokens stay visible here as authoring
        // feedback (the strips default to verbatim rendering, which the editor keeps).
        FrameworkElement BuildPreviewBar(IReadOnlyList<EditorZone> zones)
        {
            var strips = new List<PlaceholderStrip>();
            foreach (var zone in zones)
            {
                var strip = new PlaceholderStrip();
                strip.SetTemplate(zone.Get());
                strip.SetValues(new Dictionary<string, string>(values));
                strip.SetLimitWindows(limitWindows);
                zonePreviews.Add((zone, strip));
                strips.Add(strip);
            }

            FrameworkElement content;
            if (strips.Count >= 3)
            {
                // The exact control the live status bar uses, so the preview shows the same responsiveness.
                content = new ResponsiveZoneBar(strips[0], strips[1], strips[2]);
            }
            else if (strips.Count == 2)
            {
                // The title-bar layout: first zone docked left, second docked right.
                var bar = new DockPanel();
                var rightHost = strips[1].Element;
                DockPanel.SetDock(rightHost, Dock.Right);
                bar.Children.Add(rightHost);
                var leftHost = strips[0].Element;
                DockPanel.SetDock(leftHost, Dock.Left);
                bar.Children.Add(leftHost);
                content = bar;
            }
            else
            {
                content = strips.Count == 1 ? strips[0].Element : new DockPanel();
            }

            return new Border
            {
                Child = content,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 2)
            };
        }

        EditorPanel CurrentPanel() =>
            panels[Math.Max(0, Math.Min(panelCombo.SelectedIndex, panels.Count - 1))];

        // The placements offered for a panel: the window title and status bar for every panel, plus the
        // turn-status column for the chat only (it is the only panel with per-turn stats).
        List<EditorPlacement> PlacementsFor(EditorPanel panel)
        {
            if (panel.IsChat)
            {
                return
                [
                    new("Window title",
                    [
                        new("Title left", () => working.TitleLeft, v => working.TitleLeft = v),
                        new("Title right", () => working.AgentCluster, v => working.AgentCluster = v),
                    ]),
                    new("Status bar",
                    [
                        new("Status left", () => working.StatusLeft, v => working.StatusLeft = v),
                        new("Status center", () => working.StatusCenter, v => working.StatusCenter = v),
                        new("Status right", () => working.StatusRight, v => working.StatusRight = v),
                    ]),
                    new("Turn status",
                    [
                        new("Stats (one micro-stat per entry)", () => working.StatsColumn, v => working.StatsColumn = v),
                    ]),
                ];
            }

            var kind = panel.Kind;
            return
            [
                new("Window title",
                [
                    new("Title left", () => ChromeGet(kind, c => c.TitleLeft), v => ChromeFor(kind).TitleLeft = v),
                    new("Title right", () => ChromeGet(kind, c => c.TitleRight), v => ChromeFor(kind).TitleRight = v),
                ]),
                new("Status bar",
                [
                    new("Status left", () => ChromeGet(kind, c => c.StatusLeft), v => ChromeFor(kind).StatusLeft = v),
                    new("Status center", () => ChromeGet(kind, c => c.StatusCenter), v => ChromeFor(kind).StatusCenter = v),
                    new("Status right", () => ChromeGet(kind, c => c.StatusRight), v => ChromeFor(kind).StatusRight = v),
                ]),
            ];
        }

        void BuildPlacement()
        {
            zonesHost.Children.Clear();
            zonePreviews.Clear();
            currentEditors.Clear();
            var placements = PlacementsFor(CurrentPanel());
            if (placements.Count == 0)
            {
                return;
            }
            var placement = placements[Math.Max(0, Math.Min(placementCombo.SelectedIndex, placements.Count - 1))];

            foreach (var zone in placement.Zones)
            {
                zonesHost.Children.Add(FieldLabel(zone.Label));

                var editor = Inputs.text("placeholder template - type { for suggestions");
                editor.Height = double.NaN;
                editor.AcceptsReturn = false;
                suppress = true;
                editor.Text = zone.Get();
                suppress = false;
                AttachCompletion(editor);

                var capturedZone = zone;
                editor.TextChanged += (_, _) =>
                {
                    if (suppress)
                    {
                        return;
                    }
                    capturedZone.Set(editor.Text);
                    var snapshot = new Dictionary<string, string>(values);
                    foreach (var (previewZone, strip) in zonePreviews)
                    {
                        if (ReferenceEquals(previewZone, capturedZone))
                        {
                            strip.SetTemplate(editor.Text);
                            strip.SetValues(snapshot);
                        }
                    }
                };

                currentEditors.Add((zone, editor));
                zonesHost.Children.Add(editor);
            }

            zonesHost.Children.Add(FieldLabel("Preview"));
            zonesHost.Children.Add(BuildPreviewBar(placement.Zones));
        }

        // Repopulate the placement dropdown for the selected panel (their sets differ - only the chat has
        // Turn status), then rebuild the zone editors. The rebuilding flag stops the dropdown's own
        // SelectionChanged from rebuilding twice mid-repopulate.
        void RebuildPlacements()
        {
            rebuilding = true;
            placementCombo.Items.Clear();
            foreach (var placement in PlacementsFor(CurrentPanel()))
            {
                placementCombo.Items.Add(placement.Name);
            }
            placementCombo.SelectedIndex = placementCombo.Items.Count > 0 ? 0 : -1;
            rebuilding = false;
            BuildPlacement();
        }

        void RefreshPanels()
        {
            var previous = CurrentPanel().Kind;
            rebuilding = true;
            panelCombo.Items.Clear();
            foreach (var panel in panels)
            {
                panelCombo.Items.Add(panel.Name);
            }
            var index = panels.FindIndex(panel => panel.Kind == previous);
            panelCombo.SelectedIndex = panels.Count > 0 ? Math.Max(0, index) : -1;
            rebuilding = false;
            RebuildPlacements();
        }

        // A panel or placement switch first flushes the on-screen editors into the working templates, so what
        // was typed in the zone you are leaving is preserved (not dropped when the editors are rebuilt).
        panelCombo.SelectionChanged += (_, _) =>
        {
            if (!rebuilding)
            {
                FlushEditors();
                RebuildPlacements();
            }
        };
        placementCombo.SelectionChanged += (_, _) =>
        {
            if (!rebuilding)
            {
                FlushEditors();
                BuildPlacement();
            }
        };

        var saveButton = new Button
        {
            Content = "Save",
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(14, 4, 14, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        saveButton.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton");
        // Flush the on-screen editors into the working templates before serializing, so the save persists
        // exactly what is shown regardless of TextChanged timing - the cause of edits not sticking before.
        saveButton.Click += (_, _) =>
        {
            FlushEditors();
            bus.Send(new SaveConfig(StatusLineTemplates.SectionId, working.Serialize()));
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(FieldLabel("Panel"));
        root.Children.Add(panelCombo);
        root.Children.Add(FieldLabel("Placement"));
        root.Children.Add(placementCombo);
        root.Children.Add(zonesHost);
        root.Children.Add(saveButton);

        // The docking surface raises Loaded/Unloaded on every re-parent, so subscribe on load and tear down on
        // unload, re-subscribing each cycle; teardown is idempotent so a repeated Unloaded never double-disposes.
        var subs = new List<ISubscription>();

        void Subscribe()
        {
            if (subs.Count > 0)
            {
                return;
            }
            subs.Add(bus.Subscribe<RegisterPlaceholderProvider>(message =>
            {
                descriptorsByProvider[message.ProviderId] = message.Descriptors;
                return System.Threading.Tasks.Task.CompletedTask;
            }));
            subs.Add(bus.Subscribe<PanelKindRegistration>(registration =>
            {
                root.Dispatcher.InvokeAsync(() =>
                {
                    if (registration.Kind != ChatKind && panels.All(panel => panel.Kind != registration.Kind))
                    {
                        // Preserve in-flight edits before the panel list rebuild adds the new kind.
                        FlushEditors();
                        panels.Add(new EditorPanel(registration.Kind, registration.Title, false));
                        RefreshPanels();
                    }
                });
                return System.Threading.Tasks.Task.CompletedTask;
            }));
            subs.Add(bus.Subscribe<PlaceholderSnapshot>(message =>
            {
                foreach (var pair in message.Values)
                {
                    values[pair.Key] = pair.Value;
                }
                root.Dispatcher.InvokeAsync(RefreshPreviews);
                return System.Threading.Tasks.Task.CompletedTask;
            }));
            subs.Add(bus.Subscribe<AgentUsageReport>(report =>
            {
                limitWindows = report.Windows
                    .Select(window => new LimitWindow(
                        window.Name, window.Used, window.Total, window.Unit, window.WindowStart, window.ResetsAt))
                    .ToArray();
                root.Dispatcher.InvokeAsync(RefreshLimitWindows);
                return System.Threading.Tasks.Task.CompletedTask;
            }));
            subs.Add(bus.Subscribe<ConfigResult>(result =>
            {
                if (result is ConfigFound found && found.PluginId == StatusLineTemplates.SectionId)
                {
                    working = StatusLineTemplates.Parse(found.RawConfig);
                    root.Dispatcher.InvokeAsync(BuildPlacement);
                }
                return System.Threading.Tasks.Task.CompletedTask;
            }));
            subs.Add(bus.Subscribe<ConfigChanged>(changed =>
            {
                if (changed.PluginId == StatusLineTemplates.SectionId)
                {
                    working = StatusLineTemplates.Parse(changed.RawConfig);
                    root.Dispatcher.InvokeAsync(BuildPlacement);
                }
                return System.Threading.Tasks.Task.CompletedTask;
            }));
        }

        void Unsubscribe()
        {
            foreach (var sub in subs)
            {
                try { sub.Dispose(); }
                catch (ObjectDisposedException) { /* already torn down on a prior unload */ }
            }
            subs.Clear();
        }

        // The docking surface re-parents the panel often (raising Loaded each time). Re-fetching config on
        // every Loaded re-parsed the saved set and rebuilt the zones - wiping whatever was typed - and the
        // resulting ConfigResult/chrome churn could swallow a Save click. So request the catalog and config
        // exactly once; later changes still arrive live through the persistent subscriptions, and the typed
        // working set survives every re-parent.
        var requested = false;
        root.Loaded += (_, _) =>
        {
            Subscribe();
            if (!requested)
            {
                requested = true;
                bus.Send(new GetConfig(StatusLineTemplates.SectionId));
                bus.Send(new PlaceholdersRequested());
                bus.Send(new PanelKindsRequested());
            }
        };
        root.Unloaded += (_, _) => Unsubscribe();

        RefreshPanels();
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // The completion popup's child is outside the window's visual tree, where DynamicResource references can
    // fall back to defaults. These resolve the theme tokens off the connected editor (which still reaches
    // Application.Resources) and apply them as literals, so the popup honours the design's font/colours.
    private static FontFamily ResolveFontFamily(FrameworkElement source, string key) =>
        source.TryFindResource(key) as FontFamily ?? new FontFamily("Segoe UI");

    private static double ResolveDouble(FrameworkElement source, string key, double fallback) =>
        source.TryFindResource(key) is double value ? value : fallback;

    private static Brush ResolveBrush(FrameworkElement source, string key, Brush fallback) =>
        source.TryFindResource(key) as Brush ?? fallback;

    private static TextBlock FieldLabel(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 9, Margin = new Thickness(0, 10, 0, 3) };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        return block;
    }

    private sealed class CompletionRow
    {
        public CompletionRow(CompletionItem item, int replaceStart)
        {
            Item = item;
            ReplaceStart = replaceStart;
        }

        public CompletionItem Item { get; }
        public int ReplaceStart { get; }

        public override string ToString() =>
            string.IsNullOrEmpty(Item.Detail) ? Item.Label : $"{Item.Label}   -   {Item.Detail}";
    }
}
