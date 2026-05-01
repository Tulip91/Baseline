using System.Windows;
using BaseLine.Infrastructure;
using BaseLine.Services;
using BaseLine.ViewModels;
using BaseLine.Views;

namespace BaseLine;

public partial class App : Application
{
    private AppCompositionRoot? _compositionRoot;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _compositionRoot = AppCompositionRoot.Create();
        var mainWindow = new MainWindow
        {
            DataContext = _compositionRoot.Resolve<ShellViewModel>()
        };

        MainWindow = mainWindow;
        mainWindow.Show();
        _ = StartupSoundService.TryPlayAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _compositionRoot?.Dispose();
        base.OnExit(e);
    }
}
