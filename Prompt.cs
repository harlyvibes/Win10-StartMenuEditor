using System.Windows;
using System.Windows.Controls;
using StartMenuEditor.Services;

namespace StartMenuEditor;

/// <summary>Minimal modal single-line text prompt.</summary>
internal static class Prompt
{
    public static string? Show(Window owner, string title, string label, string initial = "")
    {
        var box = new TextBox { Text = initial, MinWidth = 280, Margin = new Thickness(0, 6, 0, 12) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(box);
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
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };

        return window.ShowDialog() == true ? box.Text : null;
    }
}
