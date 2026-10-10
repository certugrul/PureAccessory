using System.Windows;

namespace ERP.Updater;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow(e.Args);
        mainWindow.Show();
    }
}
