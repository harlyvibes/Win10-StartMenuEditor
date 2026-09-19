using StartMenuEditor.Models;
using StartMenuEditor.Services;
using Xunit;

namespace StartMenuEditor.Tests;

/// <summary>
/// Search filtering over a throwaway tree:
///   Empty\   Games\Chess.lnk   Games\Solitaire.lnk   Notes.lnk
/// </summary>
public sealed class NodeFilterTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "StartMenuEditorTests-" + Guid.NewGuid().ToString("N"));

    private readonly StartMenuNode _tree;

    public NodeFilterTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));
        Directory.CreateDirectory(Path.Combine(_root, "Games"));
        File.WriteAllText(Path.Combine(_root, "Games", "Chess.lnk"), "");
        File.WriteAllText(Path.Combine(_root, "Games", "Solitaire.lnk"), "");
        File.WriteAllText(Path.Combine(_root, "Notes.lnk"), "");
        _tree = StartMenuService.LoadRoot("Current user", _root)!;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private StartMenuNode Node(string name) =>
        _tree.Children.Concat(_tree.Children.SelectMany(c => c.Children)).Single(n => n.Name == name);

    [Fact]
    public void EmptyQuery_ShowsEverythingAndMatchesNothing()
    {
        _tree.ApplyFilter("chess");
        _tree.ApplyFilter("");

        Assert.All(new[] { _tree, Node("Empty"), Node("Games"), Node("Chess"), Node("Notes") },
            n => { Assert.True(n.IsVisible); Assert.False(n.IsMatch); });
        Assert.Equal(0, _tree.CountMatches());
    }

    [Fact]
    public void MatchingShortcut_KeepsItsAncestorsAndHidesTheRest()
    {
        _tree.ApplyFilter("chess");

        Assert.True(Node("Chess").IsVisible);
        Assert.True(Node("Chess").IsMatch);
        Assert.True(Node("Games").IsVisible);
        Assert.False(Node("Games").IsMatch);
        Assert.True(_tree.IsVisible);

        Assert.False(Node("Solitaire").IsVisible);
        Assert.False(Node("Notes").IsVisible);
        Assert.False(Node("Empty").IsVisible);
        Assert.Equal(1, _tree.CountMatches());
    }

    [Fact]
    public void MatchingFolder_ShowsItsWholeContents()
    {
        _tree.ApplyFilter("games");

        Assert.True(Node("Games").IsMatch);
        Assert.True(Node("Chess").IsVisible);
        Assert.True(Node("Solitaire").IsVisible);
        Assert.False(Node("Chess").IsMatch);
        Assert.False(Node("Notes").IsVisible);
        Assert.Equal(1, _tree.CountMatches());
    }

    [Theory]
    [InlineData("NOTES")]
    [InlineData("notes")]
    [InlineData("  otes ")]
    public void Search_IgnoresCaseAndSurroundingWhitespace(string query)
    {
        _tree.ApplyFilter(query);

        Assert.True(Node("Notes").IsMatch);
        Assert.False(Node("Games").IsVisible);
    }

    [Fact]
    public void NoMatches_HidesEverythingIncludingTheRoot()
    {
        _tree.ApplyFilter("zzz");

        Assert.False(_tree.IsVisible);
        Assert.False(Node("Games").IsVisible);
        Assert.Equal(0, _tree.CountMatches());
    }

    [Fact]
    public void RootName_IsNotSearchable()
    {
        _tree.ApplyFilter("current user");

        Assert.False(_tree.IsVisible);
        Assert.False(_tree.IsMatch);
    }

    [Fact]
    public void ChangingTheQuery_ReplacesThePreviousFilter()
    {
        _tree.ApplyFilter("chess");
        _tree.ApplyFilter("notes");

        Assert.True(Node("Notes").IsMatch);
        Assert.False(Node("Chess").IsVisible);
        Assert.False(Node("Chess").IsMatch);
    }

    [Fact]
    public void VisibilityChanges_RaisePropertyChanged()
    {
        var raised = new List<string?>();
        Node("Notes").PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _tree.ApplyFilter("chess");   // hides Notes
        _tree.ApplyFilter("chess");   // no change, so no notification
        _tree.ApplyFilter("");        // shows Notes again

        Assert.Equal(new[] { "IsVisible", "IsVisible" }, raised);
    }
}
