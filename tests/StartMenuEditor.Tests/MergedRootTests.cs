using StartMenuEditor.Models;
using StartMenuEditor.Services;
using Xunit;

namespace StartMenuEditor.Tests;

/// <summary>
/// Windows shows the top level of the Start Menu folder as well as its Programs subfolder in the
/// All apps list, and installers such as Corsair's use that. Layout under test:
///   StartMenu\Programs\  Games\Chess.lnk   Shared\   Notes.lnk
///   StartMenu\           Corsair\iCUE\iCUE.lnk   Shared\   Loose.lnk   readme.txt   desktop.ini
/// </summary>
public sealed class MergedRootTests : IDisposable
{
    private readonly string _startMenu =
        Path.Combine(Path.GetTempPath(), "StartMenuEditorTests-" + Guid.NewGuid().ToString("N"), "Start Menu");

    private string Programs => Path.Combine(_startMenu, "Programs");

    public MergedRootTests()
    {
        Directory.CreateDirectory(Path.Combine(Programs, "Games"));
        Directory.CreateDirectory(Path.Combine(Programs, "Shared"));
        File.WriteAllText(Path.Combine(Programs, "Games", "Chess.lnk"), "");
        File.WriteAllText(Path.Combine(Programs, "Notes.lnk"), "");

        Directory.CreateDirectory(Path.Combine(_startMenu, "Corsair", "iCUE"));
        Directory.CreateDirectory(Path.Combine(_startMenu, "Shared"));
        File.WriteAllText(Path.Combine(_startMenu, "Corsair", "iCUE", "iCUE.lnk"), "");
        File.WriteAllText(Path.Combine(_startMenu, "Loose.lnk"), "");
        File.WriteAllText(Path.Combine(_startMenu, "readme.txt"), "");
        File.WriteAllText(Path.Combine(_startMenu, "desktop.ini"), "");
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_startMenu)!, recursive: true);

    private StartMenuNode Load() => StartMenuService.LoadRoot("All users", Programs, _startMenu)!;

    private static StartMenuNode Child(StartMenuNode parent, string name) =>
        parent.Children.First(c => c.Name == name);

    // ---- Loading ---------------------------------------------------------------------------

    [Fact]
    public void Root_ShowsEntriesFromProgramsAndTheStartMenuLevel_SortedTogether()
    {
        var root = Load();

        Assert.Equal(
            new[] { "Corsair", "Games", "Shared", "Shared", "Loose", "Notes" },
            root.Children.Select(c => c.Name));
        Assert.Equal(
            new[] { NodeKind.Folder, NodeKind.Folder, NodeKind.Folder, NodeKind.Folder, NodeKind.Shortcut, NodeKind.Shortcut },
            root.Children.Select(c => c.Kind));
    }

    [Fact]
    public void FolderAtTheStartMenuLevel_KeepsItsWholeSubtree()
    {
        var corsair = Child(Load(), "Corsair");
        var icueFolder = Assert.Single(corsair.Children);
        var icueShortcut = Assert.Single(icueFolder.Children);

        Assert.Equal(NodeKind.Folder, icueFolder.Kind);
        Assert.Equal(NodeKind.Shortcut, icueShortcut.Kind);
        Assert.Equal("iCUE", icueShortcut.Name);
        Assert.True(icueShortcut.IsEditableLink);
        Assert.Equal(Path.Combine(_startMenu, "Corsair", "iCUE", "iCUE.lnk"), icueShortcut.FullPath);
    }

    [Fact]
    public void MergedEntries_AreChildrenOfTheRoot_ButKeepTheirRealPath()
    {
        var root = Load();
        var corsair = Child(root, "Corsair");

        Assert.Same(root, corsair.Parent);
        Assert.Equal(Path.Combine(_startMenu, "Corsair"), corsair.FullPath);
        Assert.Equal(Programs, root.FullPath);
    }

    [Fact]
    public void ProgramsFolder_IsNeverListedAsAFolderOfItself()
    {
        Assert.DoesNotContain(Load().Children, c => c.Name == "Programs");
    }

    [Fact]
    public void SameNamedFoldersInBothPlaces_AreBothListed()
    {
        var shared = Load().Children.Where(c => c.Name == "Shared").Select(c => c.FullPath).ToArray();

        Assert.Equal(2, shared.Length);
        Assert.Contains(Path.Combine(Programs, "Shared"), shared);
        Assert.Contains(Path.Combine(_startMenu, "Shared"), shared);
    }

    [Fact]
    public void WithoutAStartMenuPath_OnlyProgramsIsRead()
    {
        var root = StartMenuService.LoadRoot("All users", Programs)!;

        Assert.DoesNotContain(root.Children, c => c.Name == "Corsair");
        Assert.Equal(new[] { "Games", "Shared", "Notes" }, root.Children.Select(c => c.Name));
    }

    [Fact]
    public void StartMenuPathEqualToPrograms_DoesNotDuplicateEntries()
    {
        var root = StartMenuService.LoadRoot("All users", Programs, Programs + Path.DirectorySeparatorChar)!;

        Assert.Equal(new[] { "Games", "Shared", "Notes" }, root.Children.Select(c => c.Name));
    }

    [Fact]
    public void MissingStartMenuFolder_IsIgnored()
    {
        var root = StartMenuService.LoadRoot("All users", Programs, Path.Combine(_startMenu, "nope"))!;

        Assert.Equal(new[] { "Games", "Shared", "Notes" }, root.Children.Select(c => c.Name));
    }

    // ---- Editing merged entries ------------------------------------------------------------

    [Fact]
    public void Rename_StartMenuLevelFolder_RenamesItWhereItLives()
    {
        StartMenuService.Rename(Child(Load(), "Corsair"), "Corsair Gaming");

        Assert.True(File.Exists(Path.Combine(_startMenu, "Corsair Gaming", "iCUE", "iCUE.lnk")));
        Assert.False(Directory.Exists(Path.Combine(_startMenu, "Corsair")));
        Assert.False(Directory.Exists(Path.Combine(Programs, "Corsair Gaming")));
    }

    [Fact]
    public void Move_StartMenuLevelFolder_IntoAProgramsFolder()
    {
        var root = Load();
        StartMenuService.Move(Child(root, "Corsair"), Child(root, "Games"));

        Assert.True(File.Exists(Path.Combine(Programs, "Games", "Corsair", "iCUE", "iCUE.lnk")));
        Assert.False(Directory.Exists(Path.Combine(_startMenu, "Corsair")));
    }

    [Fact]
    public void Move_ShortcutIntoAStartMenuLevelFolder()
    {
        var root = Load();
        StartMenuService.Move(Child(root, "Notes"), Child(root, "Corsair"));

        Assert.True(File.Exists(Path.Combine(_startMenu, "Corsair", "Notes.lnk")));
    }

    [Fact]
    public void Move_ToTheRootItIsAlreadyShownUnder_LeavesItWhereItIs()
    {
        // Dropping Corsair on the root is a no-op: it is already listed there, and moving it into
        // Programs would silently relocate it for no visible reason.
        var root = Load();
        var corsair = Child(root, "Corsair");

        Assert.Equal(corsair.FullPath, StartMenuService.Move(corsair, root));
        Assert.True(Directory.Exists(Path.Combine(_startMenu, "Corsair")));
    }

    [Fact]
    public void Move_FromASubfolderToTheRoot_LandsInPrograms()
    {
        var root = Load();
        StartMenuService.Move(Child(Child(root, "Games"), "Chess"), root);

        Assert.True(File.Exists(Path.Combine(Programs, "Chess.lnk")));
    }

    [Fact]
    public void CreateFolder_OnTheRoot_GoesIntoPrograms()
    {
        var path = StartMenuService.CreateFolder(Load(), "Fresh");

        Assert.Equal(Path.Combine(Programs, "Fresh"), path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void SearchFilter_FindsEntriesFromTheStartMenuLevel()
    {
        var root = Load();
        root.ApplyFilter("icue");

        var icue = Child(Child(root, "Corsair"), "iCUE").Children.Single();
        Assert.True(icue.IsMatch);
        Assert.True(Child(root, "Corsair").IsVisible);
        Assert.False(Child(root, "Notes").IsVisible);
    }
}
