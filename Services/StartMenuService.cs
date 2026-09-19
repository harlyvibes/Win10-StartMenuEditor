using System.IO;
using Microsoft.VisualBasic.FileIO;
using StartMenuEditor.Models;

namespace StartMenuEditor.Services;

/// <summary>
/// File-system operations on the "All apps" list: the Programs folders under the per-user
/// and all-users Start Menu. Windows picks up changes to these folders automatically.
/// </summary>
public static class StartMenuService
{
    private static readonly string[] LinkExtensions = { ".lnk", ".url", ".appref-ms" };

    public static string UserRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");

    public static string CommonRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");

    public static IReadOnlyList<StartMenuNode> Load()
    {
        var roots = new List<StartMenuNode>();
        AddRoot(roots, "Current user", UserRoot);
        AddRoot(roots, "All users", CommonRoot);
        return roots;
    }

    public static string Rename(StartMenuNode node, string newName)
    {
        newName = ValidateName(newName);
        var isLink = node.Kind == NodeKind.Shortcut;
        var leaf = isLink ? newName + Path.GetExtension(node.FullPath) : newName;
        var target = Path.Combine(Path.GetDirectoryName(node.FullPath)!, leaf);

        var caseOnly = string.Equals(target, node.FullPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && Exists(target))
            throw new IOException($"\"{leaf}\" already exists in this folder.");

        if (isLink)
        {
            File.Move(node.FullPath, target);
        }
        else if (caseOnly)
        {
            // Directory.Move rejects a source and destination that differ only by case.
            var temp = node.FullPath + ".rename-" + Guid.NewGuid().ToString("N");
            Directory.Move(node.FullPath, temp);
            Directory.Move(temp, target);
        }
        else
        {
            Directory.Move(node.FullPath, target);
        }
        return target;
    }

    /// <summary>Sends the item to the Recycle Bin so a delete can be undone.</summary>
    public static void Delete(StartMenuNode node)
    {
        if (node.Kind == NodeKind.Shortcut)
            FileSystem.DeleteFile(node.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            FileSystem.DeleteDirectory(node.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    public static string CreateFolder(StartMenuNode parent, string name)
    {
        name = ValidateName(name);
        var path = Path.Combine(parent.FullPath, name);
        if (Exists(path)) throw new IOException($"\"{name}\" already exists in this folder.");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string Move(StartMenuNode node, StartMenuNode destination)
    {
        if (!destination.IsContainer) throw new ArgumentException("The destination must be a folder.");
        if (destination == node.Parent) return node.FullPath;

        if (node.Kind == NodeKind.Folder)
        {
            var prefix = node.FullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (destination.FullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(destination.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A folder cannot be moved into itself.");
        }

        var target = Path.Combine(destination.FullPath, Path.GetFileName(node.FullPath));
        if (Exists(target)) throw new IOException($"\"{Path.GetFileName(target)}\" already exists in the destination.");

        if (node.Kind == NodeKind.Shortcut) File.Move(node.FullPath, target);
        else Directory.Move(node.FullPath, target);
        return target;
    }

    private static void AddRoot(List<StartMenuNode> roots, string name, string path)
    {
        if (!Directory.Exists(path)) return;
        var root = new StartMenuNode(name, path, NodeKind.Root);
        Populate(root);
        roots.Add(root);
    }

    private static void Populate(StartMenuNode parent)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(parent.FullPath).OrderBy(p => LeafName(p), StringComparer.CurrentCultureIgnoreCase))
            {
                var folder = new StartMenuNode(LeafName(dir), dir, NodeKind.Folder, parent);
                parent.Children.Add(folder);
                Populate(folder);
            }

            var files = Directory.EnumerateFiles(parent.FullPath)
                .Where(f => LinkExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => LeafName(f), StringComparer.CurrentCultureIgnoreCase);
            foreach (var file in files)
                parent.Children.Add(new StartMenuNode(Path.GetFileNameWithoutExtension(file), file, NodeKind.Shortcut, parent));
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private static string LeafName(string path) => Path.GetFileName(path) ?? path;

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("The name cannot be empty.");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The name contains characters that are not allowed in file names.");
        return name;
    }
}
