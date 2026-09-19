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
        Reload();
        SetStatus(IsAdministrator()
            ? "Running as administrator."
            : "Not running as administrator - changes under \"All users\" will be refused.");
    }

    private StartMenuNode? Selected => Tree.SelectedItem as StartMenuNode;

    private void Reload()
    {
        Tree.ItemsSource = StartMenuService.Load();
        ShowDetails(null);
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
            MessageBox.Show(this, ex.Message + hint, "Start Menu Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        var answer = MessageBox.Show(this, $"Move the {what} \"{node.Name}\" to the Recycle Bin?",
            "Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
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
            MessageBox.Show(this, ex.Message, "Start Menu Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

    private static StartMenuNode? NodeAt(DependencyObject? element)
    {
        while (element is not null and not TreeViewItem)
            element = ParentOf(element);
        return (element as TreeViewItem)?.DataContext as StartMenuNode;
    }

    // Text runs are content elements, not visuals, so they have no visual parent.
    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
