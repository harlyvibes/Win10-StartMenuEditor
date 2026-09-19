using StartMenuEditor.Services;
using Xunit;

namespace StartMenuEditor.Tests;

public sealed class ThemeSettingsTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "StartMenuEditorTests-" + Guid.NewGuid().ToString("N"));

    private string SettingsFile => Path.Combine(_dir, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void DarkIsTheDefault() => Assert.Equal(AppTheme.Dark, ThemeSettings.Default);

    [Fact]
    public void Read_MissingFile_ReturnsDark() =>
        Assert.Equal(AppTheme.Dark, ThemeSettings.Read(SettingsFile));

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void WriteThenRead_RoundTrips_AndCreatesTheFolder(AppTheme theme)
    {
        ThemeSettings.Write(SettingsFile, theme);

        Assert.True(File.Exists(SettingsFile));
        Assert.Equal(theme, ThemeSettings.Read(SettingsFile));
    }

    [Fact]
    public void Write_StoresTheNameNotANumber()
    {
        ThemeSettings.Write(SettingsFile, AppTheme.Light);

        Assert.Contains("Light", File.ReadAllText(SettingsFile));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"Theme\":\"Purple\"}")]
    [InlineData("{\"Theme\":99}")]
    [InlineData("[]")]
    public void Read_CorruptOrUnknownContent_FallsBackToDark(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, content);

        Assert.Equal(AppTheme.Dark, ThemeSettings.Read(SettingsFile));
    }

    [Fact]
    public void Write_ToAnUnwritableLocation_DoesNotThrow()
    {
        // A file where a directory is needed makes CreateDirectory fail.
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "");

        var ex = Record.Exception(() => ThemeSettings.Write(Path.Combine(blocker, "settings.json"), AppTheme.Light));

        Assert.Null(ex);
    }
}
