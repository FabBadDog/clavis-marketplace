using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using FabioSoft.Clavis.Controls;

namespace FabioSoft.Nucleus.Plugins.CodeEditorPanel;

/// One node in the file tree. Directories expand lazily: a stub child is added up front so the expander
/// shows, and the real children are loaded the first time the node is expanded. Implements the shared
/// ITreeNode contract so the generic TreeBrowser can host it (Name/Children/IsExpanded are bound by
/// convention; IsLeaf marks a file as the actionable, openable node).
[ExcludeFromCodeCoverage] // WPF-bound view model
public sealed class FileNodeViewModel : INotifyPropertyChanged, ITreeNode
{
    private readonly Func<FileNodeViewModel, IReadOnlyList<FileNodeViewModel>> _loadChildren;
    private bool _isExpanded;
    private bool _isLoaded;

    public FileNodeViewModel(
        string name,
        string fullPath,
        bool isDirectory,
        Func<FileNodeViewModel, IReadOnlyList<FileNodeViewModel>> loadChildren)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        _loadChildren = loadChildren;
        Children = new ObservableCollection<FileNodeViewModel>();
        if (isDirectory)
        {
            Children.Add(new FileNodeViewModel("", "", false, _ => Array.Empty<FileNodeViewModel>()));
        }
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    /// A file is a leaf (the openable node); a directory is an expandable group, not activated on select.
    public bool IsLeaf => !IsDirectory;

    public ObservableCollection<FileNodeViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
            if (_isExpanded && !_isLoaded)
            {
                Load();
            }
        }
    }

    private void Load()
    {
        _isLoaded = true;
        Children.Clear();
        foreach (var child in _loadChildren(this))
        {
            Children.Add(child);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
