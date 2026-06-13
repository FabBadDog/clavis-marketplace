using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace FabioSoft.Nucleus.Plugins.CodeEditorPanel;

/// Builds the file tree rooted at a directory. Enumeration is per-level and lazy (driven by node
/// expansion); ordering and hidden filtering delegate to the pure FileTree helper.
[ExcludeFromCodeCoverage] // file IO + WPF-bound tree
public sealed class FileTreeViewModel
{
    private readonly bool _showHidden;

    public FileTreeViewModel(string rootPath, bool showHidden)
    {
        _showHidden = showHidden;
        Roots = new ObservableCollection<FileNodeViewModel>();
        var name = new DirectoryInfo(rootPath).Name;
        var root = new FileNodeViewModel(
            string.IsNullOrEmpty(name) ? rootPath : name, rootPath, true, LoadChildren);
        Roots.Add(root);
        root.IsExpanded = true;
    }

    public ObservableCollection<FileNodeViewModel> Roots { get; }

    private IReadOnlyList<FileNodeViewModel> LoadChildren(FileNodeViewModel node) =>
        FileTree.Order(Enumerate(node.FullPath))
            .Select(entry => new FileNodeViewModel(
                entry.Name, entry.FullPath, entry.Kind == FileNodeKind.Directory, LoadChildren))
            .ToList();

    private IReadOnlyList<FileNode> Enumerate(string path)
    {
        try
        {
            var nodes = new List<FileNode>();
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                if (!_showHidden && FileTree.IsHidden(entry.Attributes))
                {
                    continue;
                }

                var kind = entry is DirectoryInfo ? FileNodeKind.Directory : FileNodeKind.File;
                nodes.Add(new FileNode(entry.Name, entry.FullName, kind));
            }

            return nodes;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // A directory we cannot read (permissions, a vanished mount) lists as empty rather than
            // throwing through the WPF expand gesture.
            return Array.Empty<FileNode>();
        }
    }
}
