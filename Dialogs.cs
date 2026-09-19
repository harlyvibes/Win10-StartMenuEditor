using System.Windows;
using System.Windows.Controls;
using StartMenuEditor.Services;

namespace StartMenuEditor;

/// <summary>
/// Message dialogs drawn by the app, because the native MessageBox ignores the dark theme.
/// </summary>
internal static class Dialogs
{
    private const string AppTitle = "Start Menu Editor";

    public static void Warn(Window owner, string message) => Show(owner, AppTitle, message, "OK", null);

    public static bool Confirm(Window owner, string title, string message, string confirmText) =>
        Show(owner, title, message, confirmText, "Cancel");

    private static bool Show(Window owner, string title, string message, string okText, string? cancelText)
    {
        // With two buttons the action is destructive, so Cancel is the default: Enter never confirms.
        var ok = new Button { Content = okText, IsDefault = cancelText is null, IsCancel = cancelText is null, MinWidth = 80 };
        var safe = ok;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);

        if (cancelText is not null)
        {
            ok.Margin = new Thickness(0, 0, 8, 0);
            safe = new Button { Content = cancelText, IsDefault = true, IsCancel = true, MinWidth = 80 };
            buttons.Children.Add(safe);
        }

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = title,
            Content = panel,
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        ThemeService.Attach(window);
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => safe.Focus();

        return window.ShowDialog() == true;
    }
}
