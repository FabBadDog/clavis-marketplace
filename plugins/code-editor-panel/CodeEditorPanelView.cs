using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FabioSoft.Contracts.Editor;
using FabioSoft.Clavis.Controls;
using FabioSoft.Editor;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.CodeEditorPanel;

/// Builds the code-editor panel: a lazy file tree on the left and the shared CodeEditor on the right.
/// Selecting a file opens it (read off the UI thread); Ctrl+S saves. The open file and caret/selection
/// are published as EditorStateChanged (debounced); the open file path is the panel's persisted state.
[ExcludeFromCodeCoverage] // WPF construction + file IO
internal static class CodeEditorPanelView
{
    private sealed record PanelState(string FilePath);

    public static FrameworkElement Create(CodeEditorPanelConfig config, IBus bus, PanelInstanceContext context)
    {
        var rootPath = string.IsNullOrWhiteSpace(config.RootPath)
            ? Directory.GetCurrentDirectory()
            : config.RootPath;

        var editor = new CodeEditor();
        var loadedText = "";

        var dirtyDot = StatusDot.sized("ClavisBrush", 6);
        dirtyDot.Visibility = Visibility.Hidden;
        dirtyDot.Margin = new Thickness(0, 0, 6, 0);

        var pathLabel = MetadataText.create("no file open");
        pathLabel.TextTrimming = TextTrimming.CharacterEllipsis;

        var langLabel = MetadataText.create("");
        langLabel.Margin = new Thickness(8, 0, 0, 0);

        var stateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };

        void PublishState() =>
            bus.Send(new EditorStateChanged(
                editor.SourcePath, editor.Language,
                editor.CaretLine, editor.CaretColumn,
                editor.SelectionStartLine, editor.SelectionStartColumn,
                editor.SelectionEndLine, editor.SelectionEndColumn,
                editor.SelectedText));

        stateTimer.Tick += (_, _) =>
        {
            stateTimer.Stop();
            PublishState();
        };

        void ScheduleStatePublish()
        {
            stateTimer.Stop();
            stateTimer.Start();
        }

        void UpdateDirty() =>
            dirtyDot.Visibility = editor.Text != loadedText ? Visibility.Visible : Visibility.Hidden;

        void OpenFile(string path, int revealLine) =>
            Task.Run(() => File.ReadAllText(path)).ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        pathLabel.Text = $"cannot open {path}";
                        return;
                    }

                    editor.SetSourcePath(path);
                    editor.Text = task.Result;
                    loadedText = task.Result;
                    pathLabel.Text = path;
                    langLabel.Text = editor.Language;
                    dirtyDot.Visibility = Visibility.Hidden;
                    if (revealLine > 0)
                    {
                        editor.RevealLine(revealLine);
                    }

                    editor.FocusEditor();
                    PublishState();
                    context.OnStateChanged.Invoke(JsonSerializer.Serialize(new PanelState(path)));
                },
                TaskScheduler.FromCurrentSynchronizationContext());

        void Save()
        {
            var path = editor.SourcePath;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var text = editor.Text;
            Task.Run(() => File.WriteAllText(path, text)).ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        pathLabel.Text = $"save failed: {path}";
                        return;
                    }

                    loadedText = text;
                    UpdateDirty();
                },
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        editor.TextChanged += (_, _) =>
        {
            UpdateDirty();
            ScheduleStatePublish();
        };
        editor.CaretOrSelectionChanged += (_, _) => ScheduleStatePublish();

        var treeView = BuildTree(
            rootPath,
            config.ShowHiddenFiles,
            node =>
            {
                if (node is FileNodeViewModel { FullPath.Length: > 0 } file)
                {
                    OpenFile(file.FullPath, 0);
                }
            });

        var grid = BuildLayout(treeView, editor, dirtyDot, pathLabel, langLabel);

        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                Save();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && editor.Text != loadedText)
            {
                // Don't let the panel-scoped Esc (CloseActivePanel) discard unsaved edits mid-flight.
                e.Handled = true;
            }
        };

        ISubscription? openSubscription = null;
        grid.Loaded += (_, _) =>
            openSubscription ??= bus.Subscribe<OpenFileInEditor>(message =>
            {
                grid.Dispatcher.Invoke(() => OpenFile(message.FilePath, message.Line));
                return Task.CompletedTask;
            });
        grid.Unloaded += (_, _) =>
        {
            openSubscription?.Dispose();
            openSubscription = null;
            stateTimer.Stop();
        };

        var saved = DeserializeState(context.SavedState);
        if (saved is not null && File.Exists(saved.FilePath))
        {
            OpenFile(saved.FilePath, 0);
        }

        return grid;
    }

    private static Grid BuildLayout(
        TreeView treeView, CodeEditor editor, Ellipse dirtyDot, TextBlock pathLabel, TextBlock langLabel)
    {
        var grid = new Grid();
        grid.SetResourceReference(Panel.BackgroundProperty, "BlackBrush");
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220), MinWidth = 120 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });

        var treeScroller = new ScrollViewer
        {
            Content = treeView,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4)
        };
        Grid.SetColumn(treeScroller, 0);
        grid.Children.Add(treeScroller);

        var splitter = new GridSplitter
        {
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent
        };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        var editorColumn = new Grid();
        editorColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editorColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(editor, 0);
        editorColumn.Children.Add(editor);

        var status = new DockPanel { LastChildFill = true, Margin = new Thickness(6, 3, 6, 3) };
        DockPanel.SetDock(dirtyDot, Dock.Left);
        status.Children.Add(dirtyDot);
        DockPanel.SetDock(langLabel, Dock.Right);
        status.Children.Add(langLabel);
        status.Children.Add(pathLabel);
        Grid.SetRow(status, 1);
        editorColumn.Children.Add(status);

        Grid.SetColumn(editorColumn, 2);
        grid.Children.Add(editorColumn);

        return grid;
    }

    private static TreeView BuildTree(string rootPath, bool showHidden, Action<ITreeNode> onActivate)
    {
        var model = new FileTreeViewModel(rootPath, showHidden);
        return TreeBrowser.create(model.Roots, onActivate);
    }

    private static PanelState? DeserializeState(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PanelState>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
