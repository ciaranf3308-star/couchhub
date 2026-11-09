using System.Threading;
using System.Windows;
using CouchHub.Config;
using CouchHub.Input;
using CouchHub.Launcher;
using CouchHub.Services;
using Velopack;

namespace CouchHub;

/// <summary>
/// Entry point / lifecycle. Owns the singletons the windows need
/// (config, input, launcher) and enforces single-instance.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    // B1/B2: whether THIS process owns the mutex. A second launch must
    // never save config or release a mutex it does not own — OnExit
    // checks this flag and does nothing at all for a non-owner.
    private bool _ownsMutex;
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
    /// Loads the Red Silk theme dictionary. Single theme since v0.5.1:
    /// any stored theme value is ignored — there is nothing to pick.
    /// Must run before base.OnStartup: the StartupUri window is created
    /// inside base.OnStartup, and its StaticResource references resolve then.
    /// </summary>
    private void ApplyTheme()
    {
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("ThemeRedSilk.xaml", UriKind.Relative)
        });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Velopack first: handles install / update / uninstall hooks and
        // applies any update staged by a previous session, then exits
        // early for those cases. Must run before the single-instance
        // mutex, or an update apply would trip over the running copy.
        VelopackApp.Build().Run();

        // Single instance: a second launch exits silently. The already
        // running copy stays in charge of the hotkey and tray icon.
        // B1/B2: remember ownership — a non-owner must never save config
        // or release this mutex in OnExit (its Config is empty defaults).
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            Logger.Info("Second RedSilk instance detected; exiting.");
            Shutdown();
            return;
        }

        Logger.PruneOldLogs();
        Logger.Info("RedSilk LingLing Edition 0.5.1 starting.");
        Config = ConfigService.Load();
        if (Config.RecoveredFromMalformedFile)
            Logger.Warn("Config was malformed and was recovered with defaults.");
        ApplyTheme();

        XInput = new XInputService();
        Input = new InputService(XInput);
        Input.Start();

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error("Unhandled UI exception", args.Exception);
            args.Handled = true; // stay alive; the dashboard must not vanish on a UI fault
            MessageBox.Show("Something went wrong, but RedSilk is still running. " +
                            "See the log in %LOCALAPPDATA%\\CouchHub\\logs for details.",
                            "RedSilk", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("Unhandled non-UI exception", args.ExceptionObject as Exception);

        base.OnStartup(e);

        // Silent update check, fire-and-forget: stages any newer release
        // in the background (it applies on the next launch) and never
        // blocks the dashboard or crashes the app — UpdateService
        // swallows every failure into the log.
        _ = UpdateService.CheckForUpdatesAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // B1/B2: a second-launch instance owns nothing — no config save
        // (its Config is empty defaults), no mutex release (it never
        // owned it; ReleaseMutex would throw). Do nothing at all.
        if (!_ownsMutex)
        {
            base.OnExit(e);
            return;
        }
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
        Logger.Info("RedSilk exiting.");
        base.OnExit(e);
    }
}
