using System.IO;
using Microsoft.VisualBasic.FileIO;
using StartMenuEditor.Models;

namespace StartMenuEditor.Services;

/// <summary>
/// File-system operations on the "All apps" list. Windows builds that list from the per-user and
/// all-users Start Menu folders: the <c>Programs</c> subfolder and also anything sitting directly
/// in the Start Menu folder itself (installers such as Corsair's put their folders there).
/// Windows picks up changes to these folders automatically.
/// </summary>
public static class StartMenuService
{
    private static readonly string[] LinkExtensions = { ".lnk", ".url", ".appref-ms" };

    private static string UserStartMenu => Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);

    private static string CommonStartMenu => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);

    public static string UserRoot => Path.Combine(UserStartMenu, "Programs");

    public static string CommonRoot => Path.Combine(CommonStartMenu, "Programs");

    public static IReadOnlyList<StartMenuNode> Load()
    {
        var roots = new List<StartMenuNode>();
        foreach (var root in new[]
                 {
                     LoadRoot("Current user", UserRoot, UserStartMenu),
                     LoadRoot("All users", CommonRoot, CommonStartMenu),
                 })
            if (root is not null) roots.Add(root);
        return roots;
    }

    /// <summary>
    /// Loads one root, or returns null if the folder does not exist. The root's own path is the
    /// <c>Programs</c> folder (where new folders are created). If <paramref name="startMenuPath"/> is
    /// given, everything directly inside it other than the Programs folder is merged into the root
    /// as well, the way Windows merges the two locations in the All apps list.
    /// </summary>
    public static StartMenuNode? LoadRoot(string name, string path, string? startMenuPath = null)
    {
        if (!Directory.Exists(path)) return null;
        var root = new StartMenuNode(name, path, NodeKind.Root);

        var sources = new List<string> { path };
        if (startMenuPath is not null && Directory.Exists(startMenuPath) && !SamePath(startMenuPath, path))
            sources.Add(startMenuPath);

        Populate(root, sources, skipDirectory: path);
        return root;
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

    /// <summary>
    /// Fills <paramref name="parent"/> from one or more folders, folders first and then shortcuts,
    /// each sorted by name across all the sources. <paramref name="skipDirectory"/> is left out
    /// (the Programs folder, when its parent is one of the sources).
    /// </summary>
    private static void Populate(StartMenuNode parent, IEnumerable<string> sources, string? skipDirectory = null)
    {
        var folders = new List<StartMenuNode>();
        var shortcuts = new List<StartMenuNode>();

        foreach (var source in sources)
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(source))
                {
                    if (skipDirectory is not null && SamePath(dir, skipDirectory)) continue;
                    var folder = new StartMenuNode(LeafName(dir), dir, NodeKind.Folder, parent);
                    Populate(folder, new[] { dir });
                    folders.Add(folder);
                }

                foreach (var file in Directory.EnumerateFiles(source))
                {
                    if (!LinkExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                    shortcuts.Add(new StartMenuNode(Path.GetFileNameWithoutExtension(file), file, NodeKind.Shortcut, parent));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        foreach (var node in folders.Concat(shortcuts).OrderBy(n => n.Kind).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase))
            parent.Children.Add(node);
    }

    private static string LeafName(string path) => Path.GetFileName(path) ?? path;

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                      Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);

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
