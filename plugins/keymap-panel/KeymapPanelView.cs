using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using FabioSoft.Clavis.Controls;

namespace FabioSoft.Nucleus.Plugins.KeymapPanel;

/// The shortcut-management view: an add/change row at the top and a live, scope-grouped list of current
/// bindings below, each removable. Bindings and the command catalog come from the KeyMap / CommandPalette
/// broadcasts; edits go back as SetKeyBinding / RemoveKeyBinding. Gestures are typed (e.g. "Ctrl+Shift+K")
/// and normalized by the KeyMap plugin on save, so this view needs no key capture.
[ExcludeFromCodeCoverage] // WPF construction
internal static class KeymapPanelView
{
    private static readonly string[] Scopes = [KeymapScope.Application, KeymapScope.System, KeymapScope.Panel];

    // Roughly 30% smaller than the inherited default - the binding list reads as dense reference text.
    private const double RowFontSize = 8.5;

    public static FrameworkElement Create(IBus bus, PanelInstanceContext context)
    {
        IReadOnlyList<KeyBinding> bindings = [];
        IReadOnlyList<CommandDescriptor> commands = [];

        var commandCombo = Inputs.combo();
        var scopeCombo = Inputs.combo();
        scopeCombo.ItemsSource = Scopes;
        scopeCombo.SelectedIndex = 0;
        var panelBox = Inputs.text("panel kind");
        var gestureBox = Inputs.text("e.g. Ctrl+Shift+K");
        var addButton = ActionButton.create("Bind", Bind);

        var addRow = new WrapPanel { Margin = new Thickness(12, 10, 12, 6) };
        addRow.Children.Add(LabeledField.create("Command", commandCombo, 150));
        addRow.Children.Add(LabeledField.create("Scope", scopeCombo, 100));
        addRow.Children.Add(LabeledField.create("Panel", panelBox, 90));
        addRow.Children.Add(LabeledField.create("Gesture", gestureBox, 120));
        addRow.Children.Add(LabeledField.create(" ", addButton, 60));

        var list = new StackPanel();
        var scroller = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12, 4, 12, 10)
        };

        void Rebuild()
        {
            list.Children.Clear();
            foreach (var scope in Scopes)
            {
                var scoped = bindings.Where(binding => binding.Scope == scope).ToList();
                if (scoped.Count == 0)
                {
                    continue;
                }

                list.Children.Add(SectionHeader.create(scope.ToUpperInvariant()));
                foreach (var binding in scoped.OrderBy(binding => binding.Gesture, StringComparer.OrdinalIgnoreCase))
                {
                    list.Children.Add(Row(bus, binding, Describe(commands, binding.Command)));
                }
            }

            if (list.Children.Count == 0)
            {
                list.Children.Add(SectionHeader.create("NO BINDINGS"));
            }
        }

        void RefreshCommands()
        {
            var bindable = commands.Where(command => command.IsBindable)
                .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            commandCombo.ItemsSource = bindable;
            commandCombo.DisplayMemberPath = nameof(CommandDescriptor.Name);
        }

        void Bind()
        {
            if (commandCombo.SelectedItem is not CommandDescriptor command || gestureBox.Text.Trim().Length == 0)
            {
                return;
            }

            var scope = (string)(scopeCombo.SelectedItem ?? KeymapScope.Application);
            var panelKind = scope == KeymapScope.Panel ? panelBox.Text.Trim() : "";
            bus.Send(new SetKeyBinding(command.Name, scope, panelKind, gestureBox.Text.Trim()));
            gestureBox.Text = "";
        }

        ISubscription? keymapSubscription = null;
        ISubscription? commandsSubscription = null;

        var dockPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(addRow, Dock.Top);
        dockPanel.Children.Add(addRow);
        dockPanel.Children.Add(scroller);

        var root = new Border { Child = dockPanel };
        root.SetResourceReference(Border.BackgroundProperty, "BlackBrush");

        root.Loaded += (_, _) =>
        {
            keymapSubscription ??= bus.Subscribe<KeymapChanged>(changed =>
            {
                bindings = changed.Bindings;
                Application.Current?.Dispatcher.InvokeAsync(Rebuild);
                return Task.CompletedTask;
            });

            commandsSubscription ??= bus.Subscribe<CommandsAvailable>(available =>
            {
                commands = available.Commands;
                Application.Current?.Dispatcher.InvokeAsync(RefreshCommands);
                return Task.CompletedTask;
            });

            bus.Send(new RequestKeymap());
            bus.Send(new RequestCommands());
        };

        root.Unloaded += (_, _) =>
        {
            keymapSubscription?.Dispose();
            commandsSubscription?.Dispose();
            keymapSubscription = null;
            commandsSubscription = null;
        };

        return root;
    }

    private static string Describe(IReadOnlyList<CommandDescriptor> commands, string command)
    {
        var descriptor = commands.FirstOrDefault(c => string.Equals(c.Name, command, StringComparison.Ordinal));
        return descriptor is not null && !string.IsNullOrWhiteSpace(descriptor.Description)
            ? descriptor.Description
            : command;
    }

    private static FrameworkElement Row(IBus bus, KeyBinding binding, string description)
    {
        var gesture = MetadataText.accentSized(binding.Gesture, RowFontSize);
        gesture.Width = 120;

        var label = new TextBlock
        {
            Text = description,
            FontSize = RowFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        label.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var remove = IconButton.create(
            "✕", () => bus.Send(new RemoveKeyBinding(binding.Gesture, binding.Scope, binding.PanelKind)));
        DockPanel.SetDock(remove, Dock.Right);

        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(remove);
        row.Children.Add(gesture);
        row.Children.Add(label);
        return row;
    }
}
