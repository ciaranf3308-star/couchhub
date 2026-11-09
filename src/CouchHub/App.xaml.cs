using System.Threading;
using System.Windows;
using CouchHub.Config;
using CouchHub.Input;
using CouchHub.Launcher;
using CouchHub.Services;

namespace CouchHub;

/// <summary>
/// Entry point / lifecycle. Owns the singletons the windows need
/// (config, input, launcher) and enforces single-instance.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    // No "Global\" prefix: a global-namespace mutex can need a privilege
    // a normal user account does not have. The bare name is session-local,
    // which is exactly what single-instance-per-sign-in needs.
    private const string SingleInstanceMutexName = "CouchHub.SingleInstance.v1";

    public AppConfig Config { get; private set; } = new();
    public ConfigService ConfigService { get; } = new();
    public ProcessLauncher Launcher { get; } = new();
    public StartupService Startup { get; } = new();
    public InputService Input { get; private set; } = null!;
    public XInputService XInput { get; private set; } = null!;

    /// <summary>
    /// Loads the theme dictionary for the configured theme. Must run
    /// before base.OnStartup: the StartupUri window is created inside
    /// base.OnStartup, and its StaticResource references resolve then.
    /// Hotel and Bedroom define exactly the same keys (see Theme.xaml).
    /// </summary>
    private void ApplyTheme(string theme)
    {
        string file = theme == "Hotel" ? "Theme.xaml" : "ThemeBedroom.xaml";
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(file, UriKind.Relative)
        });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance: a second launch exits silently. The already
        // running copy stays in charge of the hotkey and tray icon.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            Logger.Info("Second CouchHub instance detected; exiting.");
            Shutdown();
            return;
        }

        Logger.PruneOldLogs();
        Logger.Info("CouchHub 0.3.0 starting.");
        Config = ConfigService.Load();
        if (Config.RecoveredFromMalformedFile)
            Logger.Warn("Config was malformed and was recovered with defaults.");
        ApplyTheme(Config.Theme);

        XInput = new XInputService();
        Input = new InputService(XInput);
        Input.Start();

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error("Unhandled UI exception", args.Exception);
            args.Handled = true; // stay alive; the dashboard must not vanish on a UI fault
            MessageBox.Show("Something went wrong, but CouchHub is still running. " +
                            "See the log in %LOCALAPPDATA%\\CouchHub\\logs for details.",
                            "CouchHub", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("Unhandled non-UI exception", args.ExceptionObject as Exception);

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Input?.Stop();
            Input?.Dispose();
            ConfigService.Save(Config);
        }
        catch (Exception ex)
        {
            Logger.Error("Error during shutdown", ex);
        }
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Logger.Info("CouchHub exiting.");
        base.OnExit(e);
    }
}
