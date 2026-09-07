using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace YO4X.Desktop;

public partial class App : Application
{
    private LocalServerHost? localServer;
    private bool startupCompleted;
    private bool cleanShutdownRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            DesktopEnvironmentFile.Load();
            DesktopLocalRuntime.BeginDesktopSession();
            // 1. Start In-Process Local Web & Trading Server
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            localServer = new LocalServerHost(baseDir);
            _ = Task.Run(() => localServer.StartAsync());

            // 2. Parse Launch Options pointing to in-process server
            Environment.SetEnvironmentVariable(DesktopLaunchOptions.ApplicationUrlEnvironmentVariable, localServer.BaseUrl);

            DesktopLaunchOptions options = DesktopLaunchOptions.Parse(
                e.Args,
                Environment.GetEnvironmentVariable);

            MainWindow = new MainWindow(options);
            MainWindow.Closing += (_, _) => cleanShutdownRequested = true;
            MainWindow.Show();
            startupCompleted = true;
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
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start in-process YO4X trading engine: {ex.Message}",
                "YO4X Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (startupCompleted && cleanShutdownRequested && localServer != null)
                Task.Run(() => localServer.StopAsync()).GetAwaiter().GetResult();
            else if (startupCompleted && cleanShutdownRequested)
                DesktopLocalRuntime.CompleteDesktopSession();
        }
        catch
        {
            // Exit must continue even if the local server has already stopped.
        }
        finally
        {
            base.OnExit(e);
        }
    }
}
