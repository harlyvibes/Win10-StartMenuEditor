using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace StartMenuEditor.Models;

public enum NodeKind { Root, Folder, Shortcut }

public sealed class StartMenuNode : INotifyPropertyChanged
{
    private bool _isVisible = true;
    private bool _isMatch;

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

    /// <summary>Gives tree items a readable name for screen readers and UI automation.</summary>
    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>False when the current search filter hides this node.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        private set => Set(ref _isVisible, value);
    }

    /// <summary>True when this node's own name matches the current search text.</summary>
    public bool IsMatch
    {
        get => _isMatch;
        private set => Set(ref _isMatch, value);
    }

    /// <summary>
    /// Applies a case-insensitive name search to this subtree. A node stays visible if it matches,
    /// if any descendant is visible, or if an ancestor folder matched (so a matching folder still
    /// shows its contents). Roots never match on their own name. An empty query shows everything.
    /// </summary>
    /// <returns>Whether this node is visible after filtering.</returns>
    public bool ApplyFilter(string? text, bool ancestorMatched = false)
    {
        var query = text?.Trim() ?? "";
        var filtering = query.Length > 0;
        var selfMatch = filtering && Kind != NodeKind.Root &&
                        Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);

        var anyChildVisible = false;
        foreach (var child in Children)
            if (child.ApplyFilter(query, ancestorMatched || selfMatch))
                anyChildVisible = true;

        IsMatch = selfMatch;
        IsVisible = !filtering || ancestorMatched || selfMatch || anyChildVisible;
        return IsVisible;
    }

    /// <summary>Number of nodes in this subtree whose own name matched the last filter.</summary>
    public int CountMatches() => (IsMatch ? 1 : 0) + Children.Sum(c => c.CountMatches());

    private void Set(ref bool field, bool value, [CallerMemberName] string? property = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
