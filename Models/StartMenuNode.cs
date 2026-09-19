using System.Collections.ObjectModel;
using System.IO;

namespace StartMenuEditor.Models;

public enum NodeKind { Root, Folder, Shortcut }

public sealed class StartMenuNode
{
    public StartMenuNode(string name, string fullPath, NodeKind kind, StartMenuNode? parent = null)
    {
        Name = name;
        FullPath = fullPath;
        Kind = kind;
        Parent = parent;
    }

    /// <summary>Display name; shortcuts are shown without their file extension.</summary>
    public string Name { get; }
    public string FullPath { get; }
    public NodeKind Kind { get; }
    public StartMenuNode? Parent { get; }
    public ObservableCollection<StartMenuNode> Children { get; } = new();

    public bool IsContainer => Kind != NodeKind.Shortcut;

    /// <summary>Only .lnk files can be edited; .url and .appref-ms are listed but read-only.</summary>
    public bool IsEditableLink =>
        Kind == NodeKind.Shortcut &&
        string.Equals(Path.GetExtension(FullPath), ".lnk", StringComparison.OrdinalIgnoreCase);

    public string Glyph => Kind == NodeKind.Shortcut ? "\U0001F517" : "\U0001F4C1";
}
