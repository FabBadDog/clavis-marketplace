using System.IO;

namespace FabioSoft.Nucleus.Plugins.CodeEditorPanel;

public enum FileNodeKind
{
    Directory,
    File
}

public sealed record FileNode(string Name, string FullPath, FileNodeKind Kind);

/// Pure file-tree shaping - ordering and hidden detection, no IO. The testable seam for the panel.
public static class FileTree
{
    /// Directories first, then files, each case-insensitive alphabetical.
    public static IReadOnlyList<FileNode> Order(IEnumerable<FileNode> nodes) =>
        nodes
            .OrderBy(node => node.Kind == FileNodeKind.Directory ? 0 : 1)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsHidden(FileAttributes attributes) =>
        attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
}
