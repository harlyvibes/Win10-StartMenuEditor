using StartMenuEditor.Models;
using StartMenuEditor.Services;
using Xunit;

namespace StartMenuEditor.Tests;

/// <summary>
/// Runs the service against a throwaway folder that mimics a Start menu Programs folder:
///   Empty\   Games\Chess.lnk   Notes.lnk   readme.txt
/// </summary>
public sealed class StartMenuServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "StartMenuEditorTests-" + Guid.NewGuid().ToString("N"));

    public StartMenuServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));
        Directory.CreateDirectory(Path.Combine(_root, "Games"));
        File.WriteAllText(Path.Combine(_root, "Games", "Chess.lnk"), "");
        File.WriteAllText(Path.Combine(_root, "Notes.lnk"), "");
        File.WriteAllText(Path.Combine(_root, "readme.txt"), "");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private StartMenuNode Load() => StartMenuService.LoadRoot("Test", _root)!;

    private StartMenuNode Find(StartMenuNode root, string name) =>
        root.Children.Concat(root.Children.SelectMany(c => c.Children)).Single(n => n.Name == name);

    // ---- Load ------------------------------------------------------------------------------

    [Fact]
    public void LoadRoot_MissingFolder_ReturnsNull()
    {
        Assert.Null(StartMenuService.LoadRoot("Missing", Path.Combine(_root, "nope")));
    }

    [Fact]
    public void LoadRoot_ListsFoldersBeforeShortcuts_AndSkipsOtherFiles()
    {
        var root = Load();

        Assert.Equal(new[] { "Empty", "Games", "Notes" }, root.Children.Select(c => c.Name));
        Assert.Equal(new[] { NodeKind.Folder, NodeKind.Folder, NodeKind.Shortcut }, root.Children.Select(c => c.Kind));

        var chess = Assert.Single(Find(root, "Games").Children);
        Assert.Equal("Chess", chess.Name);
        Assert.Equal(Find(root, "Games"), chess.Parent);
        Assert.True(chess.IsEditableLink);
    }

    // ---- Rename ----------------------------------------------------------------------------

    [Fact]
    public void Rename_Shortcut_KeepsExtension()
    {
        var target = StartMenuService.Rename(Find(Load(), "Notes"), "Journal");

        Assert.Equal(Path.Combine(_root, "Journal.lnk"), target);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(Path.Combine(_root, "Notes.lnk")));
    }

    [Fact]
    public void Rename_Folder_MovesItsContents()
    {
        StartMenuService.Rename(Find(Load(), "Games"), "Fun");

        Assert.True(File.Exists(Path.Combine(_root, "Fun", "Chess.lnk")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Games")));
    }

    [Fact]
    public void Rename_Folder_ChangingOnlyCase_Works()
    {
        StartMenuService.Rename(Find(Load(), "Games"), "GAMES");

        var names = Directory.EnumerateDirectories(_root).Select(d => Path.GetFileName(d));
        Assert.Contains("GAMES", names);
        Assert.DoesNotContain("Games", names);
        Assert.True(File.Exists(Path.Combine(_root, "GAMES", "Chess.lnk")));
    }

    [Fact]
    public void Rename_ShortcutToExistingShortcutName_Throws()
    {
        File.WriteAllText(Path.Combine(_root, "Other.lnk"), "");

        Assert.Throws<IOException>(() => StartMenuService.Rename(Find(Load(), "Notes"), "Other"));
        Assert.True(File.Exists(Path.Combine(_root, "Notes.lnk")));
    }

    [Fact]
    public void Rename_FolderToExistingFolderName_Throws()
    {
        Assert.Throws<IOException>(() => StartMenuService.Rename(Find(Load(), "Games"), "Empty"));
        Assert.True(Directory.Exists(Path.Combine(_root, "Games")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("what?")]
    public void Rename_InvalidName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => StartMenuService.Rename(Find(Load(), "Notes"), name));
    }

    // ---- Create ----------------------------------------------------------------------------

    [Fact]
    public void CreateFolder_CreatesDirectoryUnderParent()
    {
        var path = StartMenuService.CreateFolder(Find(Load(), "Games"), "Puzzles");

        Assert.Equal(Path.Combine(_root, "Games", "Puzzles"), path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void CreateFolder_ExistingName_Throws()
    {
        Assert.Throws<IOException>(() => StartMenuService.CreateFolder(Load(), "Games"));
    }

    // ---- Move ------------------------------------------------------------------------------

    [Fact]
    public void Move_Shortcut_IntoFolder()
    {
        var root = Load();
        var target = StartMenuService.Move(Find(root, "Notes"), Find(root, "Games"));

        Assert.Equal(Path.Combine(_root, "Games", "Notes.lnk"), target);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(Path.Combine(_root, "Notes.lnk")));
    }

    [Fact]
    public void Move_Folder_IntoAnotherFolder_KeepsContents()
    {
        var root = Load();
        StartMenuService.Move(Find(root, "Games"), Find(root, "Empty"));

        Assert.True(File.Exists(Path.Combine(_root, "Empty", "Games", "Chess.lnk")));
    }

    [Fact]
    public void Move_ToCurrentParent_IsNoOp()
    {
        var root = Load();
        var notes = Find(root, "Notes");

        Assert.Equal(notes.FullPath, StartMenuService.Move(notes, root));
        Assert.True(File.Exists(notes.FullPath));
    }

    [Fact]
    public void Move_FolderIntoItselfOrDescendant_Throws()
    {
        var root = Load();
        var games = Find(root, "Games");
        StartMenuService.CreateFolder(games, "Sub");
        var sub = Find(Load(), "Sub");

        Assert.Throws<ArgumentException>(() => StartMenuService.Move(games, games));
        Assert.Throws<ArgumentException>(() => StartMenuService.Move(games, sub));
    }

    [Fact]
    public void Move_ToShortcut_Throws()
    {
        var root = Load();
        Assert.Throws<ArgumentException>(() => StartMenuService.Move(Find(root, "Games"), Find(root, "Notes")));
    }

    [Fact]
    public void Move_NameCollision_Throws()
    {
        File.WriteAllText(Path.Combine(_root, "Games", "Notes.lnk"), "");
        var root = Load();
        var notesAtRoot = root.Children.Single(c => c.Name == "Notes");

        Assert.Throws<IOException>(() => StartMenuService.Move(notesAtRoot, Find(root, "Games")));
        Assert.True(File.Exists(Path.Combine(_root, "Notes.lnk")));
    }

    // ---- Delete (sends to the Recycle Bin, so each run leaves two tiny items there) ---------

    [Fact]
    public void Delete_Shortcut_RemovesIt()
    {
        StartMenuService.Delete(Find(Load(), "Notes"));

        Assert.False(File.Exists(Path.Combine(_root, "Notes.lnk")));
    }

    [Fact]
    public void Delete_Folder_RemovesItAndContents()
    {
        StartMenuService.Delete(Find(Load(), "Games"));

        Assert.False(Directory.Exists(Path.Combine(_root, "Games")));
    }
}
