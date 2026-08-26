using System.Windows;

namespace YO4X.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            DesktopLaunchOptions options = DesktopLaunchOptions.Parse(
                e.Args,
                Environment.GetEnvironmentVariable);
            MainWindow = new MainWindow(options);
            MainWindow.Show();
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                exception.Message,
                "YO4X desktop configuration error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(2);
        }
    }
}
