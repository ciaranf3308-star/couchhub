using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CouchHub.Config;
using CouchHub.Input;
using CouchHub.Services;
using Microsoft.Win32;

namespace CouchHub;

/// <summary>
/// Dashboard: four tiles, clock, settings entry, tray + hotkey return.
/// Input is merged in InputService (gamepad) + PreviewKeyDown (keyboard);
/// both call the same ActivateTile / MoveSelection methods, with a
/// 300 ms debounce so one press can never launch twice.
/// </summary>
public partial class MainWindow : Window
{
    private const int ReturnHomeHotkeyId = 1;
    private const int WM_HOTKEY = 0x0312;

    private readonly App _app;
    private readonly Button[] _tiles;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _epgTimer;
    private readonly EpgService _epgService = new();
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;

    private int _selectedIndex;
    private DateTime _lastActivation = DateTime.MinValue;
    private bool _hotkeyRegistered;
    private bool _settingsOpen;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        _app = (App)Application.Current;
        _tiles = new[] { GamingTile, TvTile, YouTubeTile, WindowsTile };
        _selectedIndex = Math.Clamp(_app.Config.LastFocusedTile, 0, 3);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();

        // Live guide refreshes every 30 minutes while running (and once
        // at startup, see OnLoaded). The service caches the guide file,
        // so a refresh inside the window usually reads the local cache.
        _epgTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _epgTimer.Tick += (_, _) => _ = RefreshEpgAsync();

        // Tray fallback: hidden CouchHub is always recoverable from here,
        // even if the hotkey is taken by another app.
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "CouchHub — click to return",
            Visible = false,
            Icon = System.Drawing.SystemIcons.Application
        };
        _trayIcon.DoubleClick += (_, _) => ShowDashboard();
        var trayMenu = new System.Windows.Forms.ContextMenuStrip();
        trayMenu.Items.Add("Return to CouchHub", null, (_, _) => ShowDashboard());
        trayMenu.Items.Add("Exit CouchHub", null, (_, _) => Application.Current.Shutdown());
        _trayIcon.ContextMenuStrip = trayMenu;

        // Gamepad events arrive on the input thread: marshal to the UI thread.
        _app.Input.Navigate += dir => Dispatcher.BeginInvoke(() => OnNavigate(dir));
        _app.Input.Select += () => Dispatcher.BeginInvoke(ActivateSelectedTile);
        _app.Input.Back += () => Dispatcher.BeginInvoke(OnBack);
        _app.Input.OpenSettings += () => Dispatcher.BeginInvoke(OpenSettings);
        _app.Input.ReturnHomeRequested += () => Dispatcher.BeginInvoke(ShowDashboard);
        _app.Input.ControllerPresenceChanged += connected =>
            Dispatcher.BeginInvoke(() =>
            {
                UpdateControllerIndicator(connected);
                UpdateStatus(connected
                    ? "Controller ready"
                    : "No controller detected. Keyboard and mouse work.");
            });

        SystemEvents.DisplaySettingsChanged += (_, _) => ApplyWindowMode();
        Loaded += (_, _) => OnLoaded();
        Closing += (_, _) => Cleanup();
    }

    private void OnLoaded()
    {
        ApplyWindowMode();
        ApplyScaling();
        RefreshTileSubtitles();
        UpdateClock();
        _clockTimer.Start();
        FocusSelectedTile();
        UpdateControllerIndicator(_app.Input.IsControllerConnected);

        // Live guide, fire-and-forget: the service never throws and the
        // tile degrades to badge + channel name when it has no data.
        _ = RefreshEpgAsync();
        _epgTimer.Start();

        if (_app.Config.RecoveredFromMalformedFile)
            UpdateStatus("Your settings file was damaged, so CouchHub started with defaults. " +
                         "A backup of the damaged file was kept next to config.json.");
        else
            UpdateStatus(_app.Input.IsControllerConnected
                ? "Controller ready"
                : "No controller detected. Keyboard and mouse work.");

        RegisterReturnHomeHotkey();
    }

    private void Cleanup()
    {
        _clockTimer.Stop();
        _epgTimer.Stop();
        _app.Config.LastFocusedTile = _selectedIndex;
        try { _app.ConfigService.Save(_app.Config); } catch { /* best effort */ }
        if (_hotkeyRegistered) WindowHelper.UnregisterHotkey(this, ReturnHomeHotkeyId);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    // ----- Window mode / scaling ------------------------------------------------

    private void ApplyWindowMode()
    {
        if (_app.Config.FullscreenDashboard)
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            Topmost = false;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
            Width = 1280; Height = 720;
        }
        // Re-centre on the primary display after an HDMI reconnect.
        if (WindowState == WindowState.Maximized)
        {
            Left = 0; Top = 0;
        }
    }

    private void ApplyScaling()
    {
        double scale = _app.Config.ScalingMode switch
        {
            "125" => 1.25,
            "150" => 1.5,
            "200" => 2.0,
            _ => 1.0 // "System" (or anything unknown): rely on Windows DPI scaling
        };
        RootGrid.LayoutTransform = scale == 1.0
            ? System.Windows.Media.Transform.Identity
            : new System.Windows.Media.ScaleTransform(scale, scale);
    }

    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("dddd, d MMMM yyyy");
    }

    // ----- Tiles -----------------------------------------------------------------

    private void RefreshTileSubtitles()
    {
        // The tile subtitles are static descriptors ("CONSOLES & GAMES"
        // etc.). An unconfigured launch target gets a SETUP REQUIRED
        // badge on its tile instead — the tile still opens Settings.
        GamingSetupBadge.Visibility = _app.Config.Gaming.IsConfigured
            ? Visibility.Collapsed : Visibility.Visible;
        TvSetupBadge.Visibility = _app.Config.Tv.IsConfigured
            ? Visibility.Collapsed : Visibility.Visible;
        YouTubeSetupBadge.Visibility = _app.Config.YouTube.IsConfigured
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateControllerIndicator(bool connected)
    {
        StatusDot.Fill = connected
            ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
            : (System.Windows.Media.Brush)FindResource("AccentWarmBrush");
    }

    // ----- Live TV guide -------------------------------------------------------

    private async Task RefreshEpgAsync()
    {
        EpgGuide? guide = null;
        try
        {
            guide = await _epgService.GetGuideAsync(_app.Config.Epg);
        }
        catch (Exception ex)
        {
            // The guide must never take the dashboard down.
            Logger.Warn($"Live guide refresh failed: {ex.GetType().Name}");
        }
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        Dispatcher.BeginInvoke(() => ApplyEpg(guide));
    }

    private void ApplyEpg(EpgGuide? guide)
    {
        if (guide is null)
        {
            TvNowPanel.Visibility = Visibility.Collapsed;
            TvGuidePanel.Visibility = Visibility.Collapsed;
            TvGuideHint.Visibility = Visibility.Visible;
            return;
        }

        TvGuideHint.Visibility = Visibility.Collapsed;
        if (guide.Now is not null)
        {
            TvNowPanel.Visibility = Visibility.Visible;
            TvNowTitle.Text = guide.Now.Title;
            TvNowMeta.Text = $"{guide.ChannelName}  ·  {guide.Now.StartUtc.ToLocalTime():HH:mm} – {guide.Now.EndUtc.ToLocalTime():HH:mm}";
            double total = (guide.Now.EndUtc - guide.Now.StartUtc).TotalMinutes;
            double elapsed = (DateTime.UtcNow - guide.Now.StartUtc).TotalMinutes;
            TvNowProgress.Value = total > 0 ? Math.Clamp(elapsed / total * 100, 0, 100) : 0;
        }
        else
        {
            TvNowPanel.Visibility = Visibility.Collapsed;
        }

        if (guide.Next is not null)
        {
            TvGuidePanel.Visibility = Visibility.Visible;
            TvNextTime.Text = guide.Next.StartUtc.ToLocalTime().ToString("HH:mm");
            TvNextTitle.Text = guide.Next.Title;
            if (guide.Later is not null)
            {
                TvLaterRow.Visibility = Visibility.Visible;
                TvLaterTime.Text = guide.Later.StartUtc.ToLocalTime().ToString("HH:mm");
                TvLaterTitle.Text = guide.Later.Title;
            }
            else
            {
                TvLaterRow.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            TvGuidePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void FocusSelectedTile()
    {
        _tiles[_selectedIndex].Focus();
        Keyboard.Focus(_tiles[_selectedIndex]);
    }

    private void Tile_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Button tile && int.TryParse(tile.Tag?.ToString(), out int index))
            _selectedIndex = index;
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button tile && int.TryParse(tile.Tag?.ToString(), out int index))
        {
            _selectedIndex = index;
            ActivateTile(index);
        }
    }

    private void OnNavigate(NavDirection dir)
    {
        if (_settingsOpen || !IsVisible) return;
        // Tiles sit in a single row: Left/Right move, Up/Down wrap to the ends.
        int next = dir switch
        {
            NavDirection.Left => (_selectedIndex + 3) % 4,
            NavDirection.Right => (_selectedIndex + 1) % 4,
            NavDirection.Up => 0,
            NavDirection.Down => 3,
            _ => _selectedIndex
        };
        _selectedIndex = next;
        FocusSelectedTile();
    }

    private void OnBack()
    {
        if (_settingsOpen || !IsVisible) return;
        HideDashboard(); // B / Esc on the dashboard = go to the Windows desktop
    }

    private void ActivateSelectedTile()
    {
        if (_settingsOpen || !IsVisible) return;
        ActivateTile(_selectedIndex);
    }

    private void ActivateTile(int index)
    {
        // Debounce: keyboard Enter and gamepad A can arrive for one press.
        if (DateTime.UtcNow - _lastActivation < TimeSpan.FromMilliseconds(300)) return;
        _lastActivation = DateTime.UtcNow;
        _app.Config.LastFocusedTile = index;

        if (index == 3) // Windows tile
        {
            HideDashboard();
            return;
        }

        LaunchTarget target = index switch
        {
            0 => _app.Config.Gaming,
            1 => _app.Config.Tv,
            _ => _app.Config.YouTube
        };
        if (!target.IsConfigured)
        {
            UpdateStatus($"{target.DisplayName} is not set up yet. Open Settings to choose an application.");
            OpenSettings();
            return;
        }

        var result = _app.Launcher.Launch(target);
        if (!result.Success)
        {
            UpdateStatus(result.ErrorMessage ?? "Could not start the application.");
            MessageBox.Show(result.ErrorMessage, "CouchHub",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (target.HideOnLaunch) HideDashboard();
    }

    // ----- Hide / show -----------------------------------------------------------

    /// <summary>Hide the dashboard (desktop mode / launched app). The app keeps running.</summary>
    public void HideDashboard()
    {
        _app.Input.SetPaused(true);
        _clockTimer.Stop();
        Hide();
        _trayIcon.Visible = true;
        Logger.Info("Dashboard hidden (desktop / external app).");
    }

    /// <summary>Restore the dashboard, with the previously focused tile selected.</summary>
    public void ShowDashboard()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Activate();
            FocusSelectedTile();
            return;
        }
        Show();
        WindowState = _app.Config.FullscreenDashboard ? WindowState.Maximized : WindowState.Normal;
        _trayIcon.Visible = false;
        _app.Input.SetPaused(false);
        _clockTimer.Start();
        UpdateClock();
        WindowHelper.FlashTopmost(this);
        Activate();
        FocusSelectedTile();
        Logger.Info("Dashboard restored.");
    }

    // ----- Settings --------------------------------------------------------------

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        _settingsWindow = new SettingsWindow(_app);
        _settingsWindow.Owner = this;
        bool? saved = _settingsWindow.ShowDialog();
        _settingsOpen = false;
        _settingsWindow = null;
        if (saved == true)
        {
            ApplyWindowMode();
            ApplyScaling();
            RefreshTileSubtitles();
            RegisterReturnHomeHotkey(); // shortcut text may have changed
            _ = RefreshEpgAsync(); // guide feed / channel may have changed
        }
        FocusSelectedTile();
    }

    // ----- Keyboard ---------------------------------------------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_settingsOpen) { base.OnPreviewKeyDown(e); return; }
        switch (e.Key)
        {
            case Key.Left: OnNavigate(NavDirection.Left); e.Handled = true; break;
            case Key.Right: OnNavigate(NavDirection.Right); e.Handled = true; break;
            case Key.Up: OnNavigate(NavDirection.Up); e.Handled = true; break;
            case Key.Down: OnNavigate(NavDirection.Down); e.Handled = true; break;
            case Key.Enter: ActivateSelectedTile(); e.Handled = true; break;
            case Key.Escape: OnBack(); e.Handled = true; break;
        }
        base.OnPreviewKeyDown(e);
    }

    // ----- Return-home hotkey ------------------------------------------------------

    private void RegisterReturnHomeHotkey()
    {
        var source = PresentationSource.FromVisual(this) as HwndSource;
        if (source is null)
        {
            // Not yet backed by a window handle; retry once the source exists.
            SourceInitialized += (_, _) => RegisterReturnHomeHotkey();
            return;
        }
        source.AddHook(WndProc);
        if (_hotkeyRegistered) WindowHelper.UnregisterHotkey(this, ReturnHomeHotkeyId);
        _hotkeyRegistered = WindowHelper.TryRegisterHotkey(this, ReturnHomeHotkeyId, _app.Config.ReturnHomeShortcut);
        if (!_hotkeyRegistered)
            UpdateStatus($"Could not register the return-home shortcut ({_app.Config.ReturnHomeShortcut}) — " +
                         "it may be in use by another app. The tray icon still returns home.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == ReturnHomeHotkeyId)
        {
            handled = true;
            ShowDashboard();
        }
        return IntPtr.Zero;
    }

    private void UpdateStatus(string text) => StatusText.Text = text;
}
