using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using StartMenuEditor.Models;
using StartMenuEditor.Services;

namespace StartMenuEditor;

public partial class MainWindow : Window
{
    private Point _dragStart;
    private StartMenuNode? _dragNode;
    private StartMenuNode? _current;

    public MainWindow()
    {
        InitializeComponent();
        ThemeService.Attach(this);
        Loaded += (_, _) => SearchBox.Focus();
        UpdateThemeButton();
        Reload();
        SetStatus(IsAdministrator()
            ? "Running as administrator."
            : "Not running as administrator - changes under \"All users\" will be refused.");
    }

    /// <summary>The selected node, or null if there is none or the search filter has hidden it.</summary>
    private StartMenuNode? Selected => Tree.SelectedItem is StartMenuNode { IsVisible: true } node ? node : null;

    private void Reload()
    {
        Tree.ItemsSource = StartMenuService.Load();
        ApplyFilterToTree();
        ShowDetails(null);
    }

    // ---- Search ----------------------------------------------------------------------------

    /// <summary>Applies the search box text to every root and returns the number of matching nodes.</summary>
    private int ApplyFilterToTree()
    {
        var matches = 0;
        if (Tree.ItemsSource is IEnumerable<StartMenuNode> roots)
        {
            foreach (var root in roots)
            {
                root.ApplyFilter(SearchBox.Text);
                matches += root.CountMatches();
            }
        }
        return matches;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var matches = ApplyFilterToTree();

        // Keep the details panel in step with what is visible, without discarding unsaved edits
        // when the selection has not actually changed.
        if (!ReferenceEquals(Selected, _current)) ShowDetails(Selected);

        var query = SearchBox.Text.Trim();
        SetStatus(query.Length == 0 ? "Showing all items."
            : matches == 0 ? $"No matches for \"{query}\"."
            : $"{matches} match{(matches == 1 ? "" : "es")} for \"{query}\".");
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || SearchBox.Text.Length == 0) return;
        SearchBox.Clear();
        e.Handled = true;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;
        SearchBox.Focus();
        SearchBox.SelectAll();
        e.Handled = true;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>The folder an action should apply to: the item itself, or a shortcut's parent.</summary>
    private static StartMenuNode? FolderFor(StartMenuNode? node) =>
        node is null ? null : node.IsContainer ? node : node.Parent;

    private bool Run(Action action, string success)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        {
            var hint = ex is UnauthorizedAccessException
                ? "\n\nItems under \"All users\" need the editor to be run as administrator."
                : "";
            Dialogs.Warn(this, ex.Message + hint);
            return false;
        }
        Reload();
        SetStatus(success);
        return true;
    }

    // ---- Toolbar ---------------------------------------------------------------------------

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Reload();
        SetStatus("Refreshed.");
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.Toggle();
        UpdateThemeButton();
        SetStatus(ThemeService.Current == AppTheme.Dark ? "Switched to dark mode." : "Switched to light mode.");
    }

    /// <summary>The button names the mode it will switch to.</summary>
    private void UpdateThemeButton() =>
        ThemeButton.Content = ThemeService.Current == AppTheme.Dark ? "☀  Light mode" : "☾  Dark mode";

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var parent = FolderFor(Selected);
        if (parent is null)
        {
            SetStatus("Select a folder first.");
            return;
        }
        var name = Prompt.Show(this, "New folder", "Folder name:");
        if (name is null) return;
        Run(() => StartMenuService.CreateFolder(parent, name), $"Created folder \"{name.Trim()}\".");
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var node = Selected;
        if (node is null || node.Kind == NodeKind.Root)
        {
            SetStatus("Select a folder or shortcut to rename.");
            return;
        }
        var name = Prompt.Show(this, "Rename", "New name:", node.Name);
        if (name is null) return;
        Run(() => StartMenuService.Rename(node, name), $"Renamed to \"{name.Trim()}\".");
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var node = Selected;
        if (node is null || node.Kind == NodeKind.Root)
        {
            SetStatus("Select a folder or shortcut to delete.");
            return;
        }
        var what = node.Kind == NodeKind.Folder ? "folder and everything in it" : "shortcut";
        if (!Dialogs.Confirm(this, "Delete", $"Move the {what} \"{node.Name}\" to the Recycle Bin?", "Delete")) return;
        Run(() => StartMenuService.Delete(node), $"Deleted \"{node.Name}\".");
    }

    // ---- Details panel ---------------------------------------------------------------------

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        ShowDetails(Selected);

    private void ShowDetails(StartMenuNode? node)
    {
        _current = node;
        PathText.Text = node?.FullPath ?? "";
        TargetBox.Text = ArgumentsBox.Text = WorkingDirBox.Text = DescriptionBox.Text = "";
        Details.IsEnabled = false;

        if (node is not { IsEditableLink: true }) return;
        try
        {
            var info = ShellLink.Read(node.FullPath);
            TargetBox.Text = info.TargetPath;
            ArgumentsBox.Text = info.Arguments;
            WorkingDirBox.Text = info.WorkingDirectory;
            DescriptionBox.Text = info.Description;
            Details.IsEnabled = true;
        }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException)
        {
            SetStatus($"Could not read shortcut: {ex.Message}");
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { IsEditableLink: true } node) return;
        var info = new ShortcutInfo
        {
            TargetPath = TargetBox.Text,
            Arguments = ArgumentsBox.Text,
            WorkingDirectory = WorkingDirBox.Text,
            Description = DescriptionBox.Text,
        };
        try
        {
            ShellLink.Write(node.FullPath, info);
            SetStatus($"Saved \"{node.Name}\".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Dialogs.Warn(this, ex.Message);
        }
    }

    // ---- Right-click menu and keyboard -----------------------------------------------------

    /// <summary>A right-click does not select by default, so select the entry under the cursor first.</summary>
    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt(e.OriginalSource as DependencyObject) is not { } item) return;
        item.IsSelected = true;
        item.Focus();
    }

    private void Tree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // The keyboard menu key reports no cursor position (-1) and acts on the selection.
        var fromMouse = e.CursorLeft >= 0;
        var node = fromMouse ? NodeAt(e.OriginalSource as DependencyObject) : Selected;
        if (node is null)
        {
            e.Handled = true; // nothing under the cursor, so no menu
            return;
        }

        var editable = node.Kind != NodeKind.Root; // the two roots cannot be renamed or deleted
        MenuRename.IsEnabled = editable;
        MenuDelete.IsEnabled = editable;
    }

    private void Tree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) Rename_Click(sender, e);
        else if (e.Key == Key.Delete) Delete_Click(sender, e);
        else return;
        e.Handled = true;
    }

    // ---- Drag and drop (move into a folder) ------------------------------------------------

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragNode = NodeAt(e.OriginalSource as DependencyObject);
    }

    private void Tree_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragNode is null || _dragNode.Kind == NodeKind.Root) return;

        var delta = _dragStart - e.GetPosition(null);
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var node = _dragNode;
        _dragNode = null;
        DragDrop.DoDragDrop(Tree, node, DragDropEffects.Move);
    }

    private void Tree_DragOver(object sender, DragEventArgs e)
    {
        var overNode = NodeAt(e.OriginalSource as DependencyObject);
        e.Effects = overNode is not null && e.Data.GetDataPresent(typeof(StartMenuNode))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Tree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(StartMenuNode)) is not StartMenuNode source) return;
        var destination = FolderFor(NodeAt(e.OriginalSource as DependencyObject));
        if (destination is null) return;
        Run(() => StartMenuService.Move(source, destination), $"Moved \"{source.Name}\" to \"{destination.Name}\".");
    }

    private static TreeViewItem? ItemAt(DependencyObject? element)
    {
        while (element is not null and not TreeViewItem)
            element = ParentOf(element);
        return element as TreeViewItem;
    }

    private static StartMenuNode? NodeAt(DependencyObject? element) => ItemAt(element)?.DataContext as StartMenuNode;

    // Text runs are content elements, not visuals, so they have no visual parent.
    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
