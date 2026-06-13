using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FabioSoft.Contracts.Placeholders;
using FabioSoft.Contracts.Services;
using FabioSoft.Clavis.Controls;
using FabioSoft.Clavis.Placeholders;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Conversation.Views;

// One editable zone of a surface (its alignment slot) and how to read/write its template on the working set.
internal sealed record EditorZone(string Label, Func<StatusLineTemplates, string> Get, Action<StatusLineTemplates, string> Set);

// A configurable surface (status bar / title bar / stats column) and its ordered zones. The zones ARE the
// alignment slots, so showing them together is how the editor exposes left/center/right placement.
internal sealed record EditorSurface(string Name, IReadOnlyList<EditorZone> Zones);

/// The status-line editor panel: pick a surface, then edit each of its zones (left / center / right) at once
/// with IntelliSense and a per-zone live preview, and Save - persisting to the shared "StatusLine" config the
/// chrome reads. Self-contained: it drives everything through the bus (config load/save, provider descriptors
/// for IntelliSense, value snapshots for the previews).
internal static class StatusLineEditorView
{
    private static readonly EditorSurface[] Surfaces =
    [
        new("Conversation status bar",
        [
            new("Left", t => t.StatusLeft, (t, v) => t.StatusLeft = v),
            new("Center", t => t.StatusCenter, (t, v) => t.StatusCenter = v),
            new("Right", t => t.StatusRight, (t, v) => t.StatusRight = v),
        ]),
        new("Window title bar",
        [
            new("Left (branch)", t => t.TitleLeft, (t, v) => t.TitleLeft = v),
            new("Right (agent)", t => t.AgentCluster, (t, v) => t.AgentCluster = v),
        ]),
        new("Turn stats column",
        [
            new("Stats (one micro-stat per entry)", t => t.StatsColumn, (t, v) => t.StatsColumn = v),
        ]),
    ];

    public static FrameworkElement Create(IBus bus)
    {
        var working = new StatusLineTemplates();
        var descriptorsByProvider = new Dictionary<string, IReadOnlyList<PlaceholderDescriptor>>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var zonePreviews = new List<(EditorZone Zone, PlaceholderStrip Strip)>();
        var surfaceIndex = 0;
        var suppress = false;

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

        var surfaceCombo = Inputs.combo();
        foreach (var surface in Surfaces)
        {
            surfaceCombo.Items.Add(surface.Name);
        }
        surfaceCombo.SelectedIndex = 0;

        var zonesHost = new StackPanel();

        void AttachCompletion(TextBox editor)
        {
            var list = new ListBox { MaxHeight = 240, BorderThickness = new Thickness(0) };
            list.SetResourceReference(Control.BackgroundProperty, "BlackBrush");
            // Explicit: the stock ListBox foreground is black, which disappears on the black popup.
            list.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            list.SetResourceReference(Control.FontFamilyProperty, "UiFont");
            list.FontSize = 11;
            var popup = new Popup
            {
                PlacementTarget = editor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                Child = new Border { Child = list, MinWidth = 320, BorderThickness = new Thickness(1) }
            };
            ((Border)popup.Child).SetResourceReference(Border.BorderBrushProperty, "FrameBrush");

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

        // The full-status-line preview: the surface's zones composed exactly like the real chrome (first
        // zone docked left, last docked right, a middle zone centered), so the user judges how the parts
        // read together rather than each zone in isolation.
        FrameworkElement BuildPreviewBar(EditorSurface surface)
        {
            var strips = new List<PlaceholderStrip>();
            foreach (var zone in surface.Zones)
            {
                var strip = new PlaceholderStrip();
                strip.SetTemplate(zone.Get(working));
                strip.SetValues(new Dictionary<string, string>(values));
                zonePreviews.Add((zone, strip));
                strips.Add(strip);
            }

            var bar = new DockPanel { LastChildFill = strips.Count >= 3 };
            if (strips.Count >= 2)
            {
                var rightHost = strips[^1].Element;
                DockPanel.SetDock(rightHost, Dock.Right);
                bar.Children.Add(rightHost);
            }

            var leftHost = strips[0].Element;
            DockPanel.SetDock(leftHost, Dock.Left);
            bar.Children.Add(leftHost);

            if (strips.Count >= 3)
            {
                bar.Children.Add(new Border
                {
                    Child = strips[1].Element,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            return new Border
            {
                Child = bar,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 2)
            };
        }

        void BuildSurface()
        {
            zonesHost.Children.Clear();
            zonePreviews.Clear();
            var surface = Surfaces[surfaceIndex];

            foreach (var zone in surface.Zones)
            {
                zonesHost.Children.Add(FieldLabel(zone.Label));

                var editor = Inputs.text("placeholder template - type { for suggestions");
                editor.Height = double.NaN;
                editor.AcceptsReturn = false;
                suppress = true;
                editor.Text = zone.Get(working);
                suppress = false;
                AttachCompletion(editor);

                var capturedZone = zone;
                editor.TextChanged += (_, _) =>
                {
                    if (suppress)
                    {
                        return;
                    }
                    capturedZone.Set(working, editor.Text);
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

                zonesHost.Children.Add(editor);
            }

            zonesHost.Children.Add(FieldLabel("Preview"));
            zonesHost.Children.Add(BuildPreviewBar(surface));
        }

        surfaceCombo.SelectionChanged += (_, _) =>
        {
            surfaceIndex = Math.Max(0, surfaceCombo.SelectedIndex);
            BuildSurface();
        };

        var saveButton = new Button
        {
            Content = "Save",
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(14, 4, 14, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        saveButton.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton");
        saveButton.Click += (_, _) => bus.Send(new SaveConfig(StatusLineTemplates.SectionId, working.Serialize()));

        var note = new TextBlock
        {
            Text = "Zones are the alignment slots (left / center / right). The limit-plane glyph is a separate "
                 + "usage-limits indicator, not edited here.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 9,
            Margin = new Thickness(0, 12, 0, 0)
        };
        note.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(SectionLabel("Status line editor"));
        root.Children.Add(FieldLabel("Surface"));
        root.Children.Add(surfaceCombo);
        root.Children.Add(zonesHost);
        root.Children.Add(saveButton);
        root.Children.Add(note);

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
            subs.Add(bus.Subscribe<PlaceholderSnapshot>(message =>
            {
                foreach (var pair in message.Values)
                {
                    values[pair.Key] = pair.Value;
                }
                root.Dispatcher.InvokeAsync(RefreshPreviews);
                return System.Threading.Tasks.Task.CompletedTask;
            }));
            subs.Add(bus.Subscribe<ConfigResult>(result =>
            {
                if (result is ConfigFound found && found.PluginId == StatusLineTemplates.SectionId)
                {
                    working = StatusLineTemplates.Parse(found.RawConfig);
                    root.Dispatcher.InvokeAsync(BuildSurface);
                }
                return System.Threading.Tasks.Task.CompletedTask;
            }));
            subs.Add(bus.Subscribe<ConfigChanged>(changed =>
            {
                if (changed.PluginId == StatusLineTemplates.SectionId)
                {
                    working = StatusLineTemplates.Parse(changed.RawConfig);
                    root.Dispatcher.InvokeAsync(BuildSurface);
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

        root.Loaded += (_, _) =>
        {
            Subscribe();
            bus.Send(new GetConfig(StatusLineTemplates.SectionId));
            bus.Send(new PlaceholdersRequested());
        };
        root.Unloaded += (_, _) => Unsubscribe();

        BuildSurface();
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBlock SectionLabel(string text)
    {
        var block = new TextBlock { Text = text.ToUpperInvariant(), Margin = new Thickness(0, 0, 0, 12) };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return block;
    }

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
