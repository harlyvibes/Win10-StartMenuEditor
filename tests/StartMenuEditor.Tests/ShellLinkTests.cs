using StartMenuEditor.Services;
using Xunit;

namespace StartMenuEditor.Tests;

public sealed class ShellLinkTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "StartMenuEditorTests-" + Guid.NewGuid().ToString("N"));

    public ShellLinkTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "a.exe"), "");
        File.WriteAllText(Path.Combine(_dir, "b.exe"), "");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string TargetA => Path.Combine(_dir, "a.exe");
    private string TargetB => Path.Combine(_dir, "b.exe");

    /// <summary>Creates a real .lnk through the Windows Script Host, independent of the code under test.</summary>
    private string CreateLink(string arguments = "", string description = "")
    {
        var path = Path.Combine(_dir, "test.lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var link = shell.CreateShortcut(path);
        link.TargetPath = TargetA;
        link.Arguments = arguments;
        link.Description = description;
        link.WorkingDirectory = _dir;
        link.Save();
        return path;
    }

    [Fact]
    public void Read_ReturnsAllFields()
    {
        var info = ShellLink.Read(CreateLink("--flag 1", "My comment"));

        Assert.Equal(TargetA, info.TargetPath, ignoreCase: true);
        Assert.Equal("--flag 1", info.Arguments);
        Assert.Equal("My comment", info.Description);
        Assert.Equal(_dir, info.WorkingDirectory, ignoreCase: true);
    }

    [Fact]
    public void Write_ChangesOnlyTheEditedFields()
    {
        var path = CreateLink("--old", "Old comment");
        var info = ShellLink.Read(path);
        info.Arguments = "--new";
        info.Description = "New comment";

        ShellLink.Write(path, info);

        var reread = ShellLink.Read(path);
        Assert.Equal("--new", reread.Arguments);
        Assert.Equal("New comment", reread.Description);
        Assert.Equal(TargetA, reread.TargetPath, ignoreCase: true);
        Assert.Equal(_dir, reread.WorkingDirectory, ignoreCase: true);
    }

    [Fact]
    public void Write_ChangesTarget()
    {
        var path = CreateLink();
        var info = ShellLink.Read(path);
        info.TargetPath = TargetB;

        ShellLink.Write(path, info);

        Assert.Equal(TargetB, ShellLink.Read(path).TargetPath, ignoreCase: true);
    }
}
