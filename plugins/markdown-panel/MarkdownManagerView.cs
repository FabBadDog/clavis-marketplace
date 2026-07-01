using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FabioSoft.Clavis.Controls;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

/// The delegates the manager view drives, supplied by the plugin (the single owner of the catalog, the
/// bus, and the live placeholder values). Keeps the view free of bus/config knowledge.
internal sealed class MarkdownManagerController(
    Func<IReadOnlyList<MarkdownDefinition>> getDefinitions,
    Func<IReadOnlyList<PlaceholderDescriptor>> getDescriptors,
    Func<string, string> resolve,
    Action<string> open,
    Func<string> create,
    Action<string, string, string> save,
    Action<string> delete)
{
    public Func<IReadOnlyList<MarkdownDefinition>> GetDefinitions { get; } = getDefinitions;
    public Func<IReadOnlyList<PlaceholderDescriptor>> GetDescriptors { get; } = getDescriptors;
    public Func<string, string> Resolve { get; } = resolve;
    public Action<string> Open { get; } = open;
    public Func<string> Create { get; } = create;
    public Action<string, string, string> Save { get; } = save;
    public Action<string> Delete { get; } = delete;
}

/// The "Markdown Panels" manager: a list of definitions on the left; on the right a title field, a body
/// editor with placeholder IntelliSense, a live resolved preview, and Save / Open / Delete. Create, edit,
/// and delete run through the controller; the plugin persists and pushes live refreshes (RefreshList /
/// RefreshPreview) back in.
[ExcludeFromCodeCoverage] // WPF construction
internal sealed class MarkdownManagerView
{
    private readonly MarkdownManagerController _controller;
    private readonly ListBox _list = new();
    private readonly TextBox _title = Inputs.text("panel title");
    private readonly PlaceholderEditor _body;
    private readonly MarkdownPresenter _preview = new() { Animate = false };
    private string? _selectedId;
    private bool _loading;

    public MarkdownManagerView(MarkdownManagerController controller)
    {
        _controller = controller;
        _body = new PlaceholderEditor(controller.GetDescriptors);
        Element = Build();

        _list.SelectionChanged += (_, _) => OnSelectionChanged();

        var bodyBox = (TextBox)_body.Element;
        bodyBox.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                RefreshPreview();
            }
        };
        bodyBox.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OnSave();
                args.Handled = true;
            }
        };
    }

    public FrameworkElement Element { get; }

    /// Rebuild the list from the current catalog, preserving the selected definition by id (clearing the
    /// editor if it was deleted, or selecting the first when nothing is selected yet).
    public void RefreshList()
    {
        var definitions = _controller.GetDefinitions();
        var keep = _selectedId;

        _list.Items.Clear();
        foreach (var definition in definitions)
        {
            _list.Items.Add(definition);
        }

        if (keep is not null)
        {
            var match = definitions.FirstOrDefault(definition => definition.Id == keep);
            if (match is null)
            {
                _selectedId = null;
                ClearEditor();
            }
            else
            {
                _list.SelectedItem = match;
            }
        }
        else if (definitions.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    /// Re-resolve the preview against the live placeholder values (called as values tick and as the body
    /// is edited).
    public void RefreshPreview() => _preview.Markdown = _controller.Resolve(_body.Text);

    private FrameworkElement Build()
    {
        _list.SetResourceReference(Control.BackgroundProperty, "BlackBrush");
        _list.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _list.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        _list.BorderThickness = new Thickness(0);
        _list.DisplayMemberPath = nameof(MarkdownDefinition.Title);

        var newButton = ActionButton.create("New", new Action(OnNew));
        newButton.Margin = new Thickness(0, 8, 0, 0);

        var left = new DockPanel { Width = 200, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(newButton, Dock.Bottom);
        left.Children.Add(newButton);
        left.Children.Add(_list);

        var right = new Grid();
        AddAutoRow(right);
        AddAutoRow(right);
        AddAutoRow(right);
        AddStarRow(right, 2.0);
        AddAutoRow(right);
        AddStarRow(right, 1.0);
        AddAutoRow(right);

        AddRow(right, SectionHeader.create("Title"), 0);
        AddRow(right, _title, 1);
        AddRow(right, SectionHeader.create("Body"), 2);
        AddRow(right, Framed(_body.Element), 3);
        AddRow(right, SectionHeader.create("Preview"), 4);
        var previewScroll = new ScrollViewer
        {
            Content = _preview,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8)
        };
        AddRow(right, Framed(previewScroll), 5);
        AddRow(right, BuildActions(), 6);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        grid.SetResourceReference(Panel.BackgroundProperty, "BlackBrush");
        return grid;
    }

    private StackPanel BuildActions()
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var save = ActionButton.primary("Save", new Action(OnSave));
        var open = ActionButton.create("Open", new Action(OnOpen));
        var delete = ActionButton.danger("Delete", new Action(OnDelete));
        save.Margin = new Thickness(0, 0, 8, 0);
        open.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(save);
        actions.Children.Add(open);
        actions.Children.Add(delete);
        return actions;
    }

    private static Border Framed(FrameworkElement child)
    {
        var border = new Border { Child = child, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
        border.SetResourceReference(Border.BorderBrushProperty, "FrameBrush");
        return border;
    }

    private static void AddAutoRow(Grid grid) =>
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    private static void AddStarRow(Grid grid, double weight) =>
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(weight, GridUnitType.Star) });

    private static void AddRow(Grid grid, FrameworkElement element, int row)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }

    private void OnSelectionChanged()
    {
        if (_list.SelectedItem is MarkdownDefinition definition)
        {
            _selectedId = definition.Id;
            LoadEditor(definition);
        }
    }

    private void LoadEditor(MarkdownDefinition definition)
    {
        _loading = true;
        _title.Text = definition.Title;
        _body.Text = definition.Body;
        _loading = false;
        RefreshPreview();
    }

    private void ClearEditor()
    {
        _loading = true;
        _title.Text = "";
        _body.Text = "";
        _loading = false;
        _preview.Markdown = "";
    }

    private void OnNew()
    {
        _selectedId = _controller.Create();
        RefreshList();
        _title.Focus();
    }

    private void OnSave()
    {
        if (_selectedId is null)
        {
            return;
        }

        _controller.Save(_selectedId, _title.Text, _body.Text);
        RefreshPreview();
    }

    private void OnOpen()
    {
        if (_selectedId is not null)
        {
            _controller.Open(_selectedId);
        }
    }

    private void OnDelete()
    {
        if (_selectedId is null)
        {
            return;
        }

        var id = _selectedId;
        _selectedId = null;
        _controller.Delete(id);
        ClearEditor();
        RefreshList();
    }
}
