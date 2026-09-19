using System.Windows;
using StartMenuEditor.Services;

namespace StartMenuEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeService.Initialize();
        base.OnStartup(e);
    }
}
