using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StartMenuEditor.Services;

public enum AppTheme { Dark, Light }

/// <summary>Reads and writes the saved theme choice. Any problem falls back to the dark default.</summary>
public static class ThemeSettings
{
    public const AppTheme Default = AppTheme.Dark;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StartMenuEditor", "settings.json");

    public static AppTheme Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return Default;
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path));
            return data is not null && Enum.IsDefined(data.Theme) ? data.Theme : Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Default;
        }
    }

    /// <summary>Saves the choice; failing to save must never break the app, so errors are swallowed.</summary>
    public static void Write(string path, AppTheme theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new Data { Theme = theme }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Data
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AppTheme Theme { get; set; } = Default;
    }
}
