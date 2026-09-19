using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace StartMenuEditor.Services;

/// <summary>
/// Swaps the colour dictionary (Themes\DarkTheme.xaml / LightTheme.xaml) that every control style
/// refers to via DynamicResource, and darkens the native title bar to match.
/// </summary>
public static class ThemeService
{
    private const int DwmUseImmersiveDarkMode = 20;       // Windows 10 2004 and later
    private const int DwmUseImmersiveDarkModePre2004 = 19; // Windows 10 1809 and 1903

    public static AppTheme Current { get; private set; } = ThemeSettings.Default;

    /// <summary>Applies the saved theme (dark by default). Call before any window is created.</summary>
    public static void Initialize()
    {
        Current = ThemeSettings.Read(ThemeSettings.DefaultPath);
        Apply(Current);
    }

    public static void Toggle() => Set(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    public static void Set(AppTheme theme)
    {
        Current = theme;
        Apply(theme);
        ThemeSettings.Write(ThemeSettings.DefaultPath, theme);
        foreach (Window window in Application.Current.Windows) ApplyTitleBar(window);
    }

    /// <summary>
    /// Themes a window: its colours follow the current theme, and its title bar is darkened before
    /// the window first appears (Windows 10 does not repaint the frame if this is done afterwards).
    /// </summary>
    public static void Attach(Window window)
    {
        window.SetResourceReference(Control.BackgroundProperty, "Brush.Window");
        window.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        window.SourceInitialized += (_, _) => ApplyTitleBar(window);
    }

    private static void Apply(AppTheme theme)
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{theme}Theme.xaml", UriKind.Absolute),
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dictionary; // slot 0 always holds the colour dictionary
        else merged.Insert(0, dictionary);
    }

    private static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var dark = Current == AppTheme.Dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModePre2004, ref dark, sizeof(int));

        // A window that is not shown yet paints correctly on first show. A visible one keeps its old
        // caption until its activation state changes (SetWindowPos and RedrawWindow do not help),
        // so flip it off and on again, ending in the state it started in.
        if (!window.IsVisible) return;
        var active = window.IsActive;
        SendMessage(handle, WmNcActivate, active ? IntPtr.Zero : (IntPtr)1, IntPtr.Zero);
        SendMessage(handle, WmNcActivate, active ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
    }

    private const uint WmNcActivate = 0x0086;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
