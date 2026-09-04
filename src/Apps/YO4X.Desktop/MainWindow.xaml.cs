using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace YO4X.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopLaunchOptions options;
    private readonly DesktopNavigationPolicy navigationPolicy;

    internal MainWindow(DesktopLaunchOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        navigationPolicy = new DesktopNavigationPolicy(
            options.ApplicationUri,
            options.IdentityProviderUri);
        InitializeComponent();
        ApplyWindowIcon();
        bool showDevelopmentChrome = options.StartInDevelopmentFixture;
        CommandBar.Visibility = showDevelopmentChrome ? Visibility.Visible : Visibility.Collapsed;
        StatusBar.Visibility = showDevelopmentChrome ? Visibility.Visible : Visibility.Collapsed;
        CommandBarRow.Height = showDevelopmentChrome ? new GridLength(52) : new GridLength(0);
        StatusBarRow.Height = showDevelopmentChrome ? new GridLength(28) : new GridLength(0);
        BrowserHost.Margin = showDevelopmentChrome ? new Thickness(10) : new Thickness(0);
        BrowserHost.BorderThickness = showDevelopmentChrome ? new Thickness(1) : new Thickness(0);
        BrowserHost.CornerRadius = showDevelopmentChrome ? new CornerRadius(8) : new CornerRadius(0);
        FixtureButton.Visibility = showDevelopmentChrome ? Visibility.Visible : Visibility.Collapsed;
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= MainWindow_SourceInitialized;
        ApplyNativeWindowIcon();
    }

    private void ApplyNativeWindowIcon()
    {
        string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
        if (!File.Exists(icoPath))
        {
            return;
        }

        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }

        nint big = LoadImage(0, icoPath, ImageIcon, 256, 256, LoadFromFile);
        nint small = LoadImage(0, icoPath, ImageIcon, 16, 16, LoadFromFile);
        if (big != 0)
        {
            SendMessage(hwnd, WmSetIcon, IconBig, big);
        }

        if (small != 0)
        {
            SendMessage(hwnd, WmSetIcon, IconSmall, small);
        }
    }

    private void ApplyWindowIcon()
    {
        string[] iconCandidates =
        [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "assets", "yo4x-icon.png"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico"),
        ];
        foreach (string iconPath in iconCandidates)
        {
            if (!File.Exists(iconPath))
            {
                continue;
            }

            try
            {
                using FileStream stream = File.OpenRead(iconPath);
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                Icon = image;
                return;
            }
            catch
            {
            }
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        try
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YO4X",
                "Desktop",
                "WebView2");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            ConfigureBrowser(Browser.CoreWebView2);
            await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "Object.defineProperty(window,'__YO4X_DESKTOP_SHELL__',{value:true,writable:false,configurable:false});");
            Navigate(options.InitialUri);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowStartupFailure(
                "Microsoft Edge WebView2 Runtime is required. Install the Evergreen WebView2 Runtime and restart YO4X.");
        }
        catch (Exception)
        {
            ShowStartupFailure("YO4X Desktop could not initialize its secure browser surface.");
        }
    }

    private void ConfigureBrowser(CoreWebView2 core)
    {
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
#if !DEBUG
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.NavigationStarting += Core_NavigationStarting;
        core.FrameNavigationStarting += Core_FrameNavigationStarting;
        core.NewWindowRequested += Core_NewWindowRequested;
        core.DownloadStarting += Core_DownloadStarting;
        core.PermissionRequested += Core_PermissionRequested;
        core.BasicAuthenticationRequested += Core_BasicAuthenticationRequested;
        core.ServerCertificateErrorDetected += Core_ServerCertificateErrorDetected;
        core.NavigationCompleted += Core_NavigationCompleted;
        core.HistoryChanged += Core_HistoryChanged;
        core.ProcessFailed += Core_ProcessFailed;
        core.WebMessageReceived += Core_WebMessageReceived;
    }

    private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try
        {
            raw = e.WebMessageAsJson;
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("type", out JsonElement type)
                || !string.Equals(type.GetString(), "yo4x-window", StringComparison.Ordinal)
                || !root.TryGetProperty("command", out JsonElement command))
            {
                return;
            }

            string? windowCommand = command.GetString();
            _ = Dispatcher.InvokeAsync(() =>
            {
                switch (windowCommand)
                {
                    case "minimise":
                        WindowState = WindowState.Minimized;
                        break;
                    case "maximise":
                        WindowState = WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized;
                        break;
                    case "close":
                        Close();
                        break;
                }
            });
        }
        catch (JsonException)
        {
        }
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? requested)
            && navigationPolicy.IsAllowedInShell(requested))
        {
            StatusText.Text = "Loading the configured YO4X frontend…";
            return;
        }

        e.Cancel = true;
        if (requested is not null && DesktopNavigationPolicy.CanOpenExternally(requested))
        {
            Process.Start(new ProcessStartInfo(requested.AbsoluteUri) { UseShellExecute = true });
            StatusText.Text = "Opened the external HTTPS page in your default browser.";
        }
        else
        {
            StatusText.Text = "Blocked navigation outside the configured YO4X origin.";
        }
    }

    private void Core_FrameNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? requested)
            || !navigationPolicy.IsAllowedInShell(requested))
        {
            e.Cancel = true;
            StatusText.Text = "Blocked a frame outside the configured YO4X origin.";
        }
    }

    private void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? requested))
        {
            StatusText.Text = "Blocked an invalid popup request.";
            return;
        }

        if (navigationPolicy.IsAllowedInShell(requested))
        {
            Navigate(requested);
            return;
        }

        if (DesktopNavigationPolicy.CanOpenExternally(requested))
        {
            Process.Start(new ProcessStartInfo(requested.AbsoluteUri) { UseShellExecute = true });
            StatusText.Text = "Opened the external HTTPS page in your default browser.";
            return;
        }

        StatusText.Text = "Blocked a popup outside the configured YO4X origin.";
    }

    private void Core_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        e.Handled = true;
        StatusText.Text = "Downloads are disabled in the YO4X desktop shell.";
    }

    private void Core_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
        e.Handled = true;
        StatusText.Text = "A browser permission request was denied.";
    }

    private void Core_BasicAuthenticationRequested(
        object? sender,
        CoreWebView2BasicAuthenticationRequestedEventArgs e)
    {
        e.Cancel = true;
        StatusText.Text = "A browser credential challenge was blocked.";
    }

    private void Core_ServerCertificateErrorDetected(
        object? sender,
        CoreWebView2ServerCertificateErrorDetectedEventArgs e)
    {
        if (IsPinnedDevelopmentIdentityCertificate(e))
        {
            e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
            return;
        }

        e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        StatusText.Text = "Navigation with an invalid TLS certificate was blocked.";
    }

    private bool IsPinnedDevelopmentIdentityCertificate(
        CoreWebView2ServerCertificateErrorDetectedEventArgs e)
    {
        if (options.DevelopmentIdentityCertificateSha256 is null
            || options.IdentityProviderUri is null
            || e.ErrorStatus != CoreWebView2WebErrorStatus.CertificateIsInvalid
            || !Uri.TryCreate(e.RequestUri, UriKind.Absolute, out Uri? requested)
            || requested.Scheme != Uri.UriSchemeHttps
            || requested.Host != options.IdentityProviderUri.Host
            || requested.Port != options.IdentityProviderUri.Port)
        {
            return false;
        }

        try
        {
            using X509Certificate2 certificate = X509Certificate2.CreateFromPem(
                e.ServerCertificate.ToPemEncoding());
            string observed = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(observed),
                Convert.FromHexString(options.DevelopmentIdentityCertificateSha256));
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        StatusText.Text = e.IsSuccess
            ? Browser.Source.Query.Contains("fixture=dashboard", StringComparison.Ordinal)
                ? "Development fixture — visual testing only; no backend authority."
                : "Loaded the configured YO4X frontend origin."
            : "The YO4X frontend is unavailable. Start the local services or verify the configured HTTPS origin.";
        UpdateHistoryButtons();
    }

    private void Core_HistoryChanged(object? sender, object e) => UpdateHistoryButtons();

    private void Core_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e) =>
        StatusText.Text = "The embedded browser process stopped unexpectedly. Refresh to recover.";

    private void UpdateHistoryButtons()
    {
        BackButton.IsEnabled = Browser.CanGoBack;
        ForwardButton.IsEnabled = Browser.CanGoForward;
    }

    private void Navigate(Uri uri)
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        Browser.CoreWebView2.Navigate(uri.AbsoluteUri);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CanGoBack)
        {
            Browser.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CanGoForward)
        {
            Browser.GoForward();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Browser.Reload();

    private void LiveButton_Click(object sender, RoutedEventArgs e) => Navigate(options.ApplicationUri);

    private void FixtureButton_Click(object sender, RoutedEventArgs e)
    {
        if (options.DevelopmentFixtureUri is not null)
        {
            Navigate(options.DevelopmentFixtureUri);
        }
    }

    private void ShowStartupFailure(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(message, "YO4X Desktop", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private const int WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
}
