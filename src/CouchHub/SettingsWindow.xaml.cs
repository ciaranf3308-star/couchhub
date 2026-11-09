using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CouchHub.Config;
using CouchHub.Input;
using CouchHub.Launcher;
using CouchHub.Services;
using Microsoft.Win32;

namespace CouchHub;

/// <summary>
/// Controller-navigable settings. Gamepad input is routed here while
/// the dialog is open: D-pad moves WPF focus, A activates the focused
/// control, B closes. Keyboard arrows/Enter/Escape work natively.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly DispatcherTimer _controllerTimer;
    private readonly EpgService _epgService = new();
    private string _epgFeedUrl = EpgSettings.IrelandFeedUrl;
    private string _epgChannelId = "RTÉOne.ie";
    private string _epgChannelName = "RTÉ One";
    private bool _populatingEpg;
    // R2: monotonic id for channel-list loads — a stale load that
    // finishes after a newer one must not populate the combo or
    // overwrite _epgChannelId.
    private int _epgLoadId;

    public SettingsWindow(App app)
    {
        InitializeComponent();
        _app = app;

        GamingPathBox.Text = _app.Config.Gaming.ExePath;
        GamingArgsBox.Text = _app.Config.Gaming.Arguments;
        TvPathBox.Text = _app.Config.Tv.ExePath;
        TvArgsBox.Text = _app.Config.Tv.Arguments;
        YouTubePathBox.Text = _app.Config.YouTube.ExePath;
        YouTubeArgsBox.Text = _app.Config.YouTube.Arguments;
        FullscreenCheck.IsChecked = _app.Config.FullscreenDashboard;
        StartupCheck.IsChecked = _app.Startup.IsEnabled;
        HotkeyBox.Text = _app.Config.ReturnHomeShortcut;
        foreach (ComboBoxItem item in ScalingCombo.Items)
        {
            if ((item.Tag as string) == _app.Config.ScalingMode)
                ScalingCombo.SelectedItem = item;
        }
        ScalingCombo.SelectedItem ??= ScalingCombo.Items[0];

        _epgFeedUrl = _app.Config.Epg.FeedUrl;
        _epgChannelId = _app.Config.Epg.ChannelId;
        _epgChannelName = _app.Config.Epg.ChannelName;
        _populatingEpg = true;
        SelectFeedCombo(_epgFeedUrl);
        _populatingEpg = false;
        EpgXtreamServerBox.Text = _app.Config.Epg.XtreamServerUrl;
        EpgXtreamUserBox.Text = _app.Config.Epg.XtreamUsername;
        EpgXtreamPasswordBox.Password = _app.Config.Epg.XtreamPassword;
        EpgXtreamChannelBox.Text = _app.Config.Epg.XtreamChannelName;
        // Channel list is lazy: Settings must not download the full
        // guide on open. Show just the configured channel until the
        // user opens the guide section or hits Refresh channels.
        EpgChannelCombo.Items.Add(new ComboBoxItem { Content = _epgChannelName, Tag = _epgChannelId });
        EpgChannelCombo.SelectedItem = EpgChannelCombo.Items[0];

        _controllerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _controllerTimer.Tick += (_, _) => RefreshControllerStatus();
        RefreshControllerStatus();
        UpdateStatusText.Text = $"Current version {CurrentVersion}.";
        Loaded += (_, _) => _controllerTimer.Start();
        Closed += (_, _) =>
        {
            _controllerTimer.Stop();
            UnsubscribeGamepad();
        };

        SubscribeGamepad();
        GamingPathBox.Focus();
    }

    private static string CurrentVersion =>
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking for updates…";
        try
        {
            var result = await UpdateService.CheckForUpdatesAsync();
            UpdateStatusText.Text = result.Message;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void RefreshControllerStatus()
    {
        if (!_app.Input.IsXInputAvailable)
        {
            ControllerStatusText.Text = "No XInput controller support was found on this PC. " +
                                        "Keyboard and mouse navigation still work.";
            return;
        }
        ControllerStatusText.Text = _app.Input.IsControllerConnected
            ? $"Controller connected ({_app.Input.XInputDll}). D-pad / stick to move, A to select, B to close."
            : $"No controller connected right now ({_app.Input.XInputDll} loaded). " +
              "Connect an Xbox controller and it will work without restarting RedSilk.";
    }

    // ----- Live TV guide -------------------------------------------------------

    private void SelectFeedCombo(string url)
    {
        foreach (ComboBoxItem item in EpgFeedCombo.Items)
        {
            if (string.Equals((string?)item.Tag, url, StringComparison.OrdinalIgnoreCase))
            {
                EpgFeedCombo.SelectedItem = item;
                return;
            }
        }
        // A hand-edited config may hold a custom feed URL: keep it as a
        // selectable item instead of silently dropping it.
        var custom = new ComboBoxItem { Content = HostOf(url) + " (custom)", Tag = url };
        EpgFeedCombo.Items.Add(custom);
        EpgFeedCombo.SelectedItem = custom;
    }

    private static string HostOf(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url; }
    }

    private async Task LoadEpgChannelsAsync()
    {
        int loadId = ++_epgLoadId;
        var channels = await _epgService.GetChannelsAsync(_epgFeedUrl);
        if (loadId != _epgLoadId) return; // a newer load superseded this one
        if (channels.Count == 0) channels = EpgService.PresetChannels;
        _populatingEpg = true;
        EpgChannelCombo.Items.Clear();
        foreach (var ch in channels)
            EpgChannelCombo.Items.Add(new ComboBoxItem { Content = ch.Name, Tag = ch.Id });
        // Keep the configured channel selectable even if this guide
        // file does not list it (e.g. the feed was just switched).
        bool found = false;
        foreach (ComboBoxItem item in EpgChannelCombo.Items)
        {
            if (string.Equals((string?)item.Tag, _epgChannelId, StringComparison.OrdinalIgnoreCase))
            {
                EpgChannelCombo.SelectedItem = item;
                found = true;
                break;
            }
        }
        if (!found)
        {
            var current = new ComboBoxItem { Content = _epgChannelName, Tag = _epgChannelId };
            EpgChannelCombo.Items.Add(current);
            EpgChannelCombo.SelectedItem = current;
        }
        var selected = (ComboBoxItem?)EpgChannelCombo.SelectedItem;
        if (selected is not null)
        {
            _epgChannelId = (string)selected.Tag;
            _epgChannelName = (string)selected.Content;
        }
        _populatingEpg = false;
    }

    private void EpgFeedCombo_DropDownOpened(object sender, EventArgs e) => _ = LoadEpgChannelsAsync();

    private void EpgChannelCombo_DropDownOpened(object sender, EventArgs e) => _ = LoadEpgChannelsAsync();

    private void EpgFeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populatingEpg) return;
        var item = (ComboBoxItem?)EpgFeedCombo.SelectedItem;
        if (item?.Tag is not string url) return;
        _epgFeedUrl = url;
        _ = LoadEpgChannelsAsync();
    }

    private void EpgChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populatingEpg) return;
        var item = (ComboBoxItem?)EpgChannelCombo.SelectedItem;
        if (item is null) return;
        _epgChannelId = (string)item.Tag;
        _epgChannelName = (string)item.Content;
        // A .uk channel needs the UK guide and a .ie channel the
        // Ireland guide — switch the feed to match.
        string wantFeed = _epgChannelId.EndsWith(".uk", StringComparison.OrdinalIgnoreCase)
            ? EpgSettings.UkFeedUrl
            : _epgChannelId.EndsWith(".ie", StringComparison.OrdinalIgnoreCase)
                ? EpgSettings.IrelandFeedUrl
                : _epgFeedUrl;
        if (!string.Equals(wantFeed, _epgFeedUrl, StringComparison.OrdinalIgnoreCase))
        {
            _epgFeedUrl = wantFeed;
            _populatingEpg = true;
            SelectFeedCombo(_epgFeedUrl);
            _populatingEpg = false;
        }
    }

    private void EpgRefreshChannels_Click(object sender, RoutedEventArgs e) => _ = LoadEpgChannelsAsync();

    private EpgSettings CurrentEpgSettings() => new()
    {
        FeedUrl = _epgFeedUrl,
        ChannelId = _epgChannelId,
        ChannelName = _epgChannelName,
        XtreamServerUrl = EpgXtreamServerBox.Text.Trim(),
        XtreamUsername = EpgXtreamUserBox.Text.Trim(),
        XtreamPassword = EpgXtreamPasswordBox.Password,
        XtreamChannelName = string.IsNullOrWhiteSpace(EpgXtreamChannelBox.Text)
            ? _epgChannelName : EpgXtreamChannelBox.Text.Trim()
    };

    private async void TestGuide_Click(object sender, RoutedEventArgs e)
    {
        TestGuideButton.IsEnabled = false;
        EpgTestStatusText.Text = "Checking the guide…";
        try
        {
            var guide = await _epgService.GetGuideAsync(CurrentEpgSettings());
            if (guide?.Now is not null)
                EpgTestStatusText.Text = $"Found: {guide.Now.Title} on {guide.ChannelName} " +
                    $"({guide.Now.StartUtc.ToLocalTime():HH:mm}–{guide.Now.EndUtc.ToLocalTime():HH:mm}), via {guide.Source}.";
            else if (guide?.Next is not null)
                EpgTestStatusText.Text = $"Nothing on right now — next is {guide.Next.Title} on {guide.ChannelName} " +
                    $"at {guide.Next.StartUtc.ToLocalTime():HH:mm}, via {guide.Source}.";
            else
                EpgTestStatusText.Text = "No guide data for that channel — check the channel and guide, then try again.";
        }
        catch (Exception ex)
        {
            // R1: a malformed URL must land here as a plain message,
            // never an unhandled async-void crash.
            Logger.Warn($"Test guide failed: {ex.GetType().Name}");
            EpgTestStatusText.Text = "Guide check failed — check the guide and provider URLs, then try again.";
        }
        finally
        {
            TestGuideButton.IsEnabled = true;
        }
    }

    // ----- Gamepad routing -------------------------------------------------------

    private void SubscribeGamepad()
    {
        _app.Input.Navigate += OnGamepadNavigate;
        _app.Input.Select += OnGamepadSelect;
        _app.Input.Back += OnGamepadBack;
        _app.Input.ReturnHomeRequested += OnGamepadBack;
    }

    private void UnsubscribeGamepad()
    {
        _app.Input.Navigate -= OnGamepadNavigate;
        _app.Input.Select -= OnGamepadSelect;
        _app.Input.Back -= OnGamepadBack;
        _app.Input.ReturnHomeRequested -= OnGamepadBack;
    }

    private void OnGamepadNavigate(NavDirection dir)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var request = new TraversalRequest(dir switch
            {
                NavDirection.Up => FocusNavigationDirection.Up,
                NavDirection.Down => FocusNavigationDirection.Down,
                NavDirection.Left => FocusNavigationDirection.Previous,
                _ => FocusNavigationDirection.Next
            });
            (Keyboard.FocusedElement as UIElement)?.MoveFocus(request);
        });
    }

    private void OnGamepadSelect()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (Keyboard.FocusedElement is Button button)
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
    }

    private void OnGamepadBack()
    {
        Dispatcher.BeginInvoke(() => { DialogResult = false; Close(); });
    }

    // ----- Browse ------------------------------------------------------------------

    private void BrowseInto(TextBox target)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Applications (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            target.Text = dialog.FileName;
    }

    private void BrowseGaming_Click(object sender, RoutedEventArgs e) => BrowseInto(GamingPathBox);
    private void BrowseTv_Click(object sender, RoutedEventArgs e) => BrowseInto(TvPathBox);
    private void BrowseYouTube_Click(object sender, RoutedEventArgs e) => BrowseInto(YouTubePathBox);

    // ----- Save / Cancel / Exit ------------------------------------------------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        // Validate only targets the user actually filled in, using the
        // same resolver the launcher uses — so a Store-app target such
        // as "shell:AppsFolder\...!App" (HotPlayer) saves fine even
        // though it is not a file on disk.
        foreach ((string label, LaunchTarget target) in new[]
                 {
                     ("Gaming", new LaunchTarget { DisplayName = "Gaming", ExePath = GamingPathBox.Text }),
                     ("TV", new LaunchTarget { DisplayName = "TV", ExePath = TvPathBox.Text }),
                     ("YouTube", new LaunchTarget { DisplayName = "YouTube", ExePath = YouTubePathBox.Text })
                 })
        {
            if (string.IsNullOrWhiteSpace(target.ExePath)) continue;
            if (!ProcessLauncher.TryResolve(target, out string error))
            {
                ErrorText.Text = error;
                return;
            }
        }

        // R1: a malformed guide / provider URL must fail here with a
        // plain message — never crash "Test guide" later. Same style
        // as the launch-target validation above.
        if (!string.IsNullOrWhiteSpace(_epgFeedUrl) && !IsValidHttpUrl(_epgFeedUrl))
        {
            ErrorText.Text = "The guide URL is not a valid http(s) address.";
            return;
        }
        if (!string.IsNullOrWhiteSpace(EpgXtreamServerBox.Text) &&
            !IsValidHttpUrl(EpgXtreamServerBox.Text.Trim()))
        {
            ErrorText.Text = "The provider server URL is not a valid http(s) address.";
            return;
        }

        _app.Config.Gaming.ExePath = GamingPathBox.Text.Trim();
        _app.Config.Gaming.Arguments = GamingArgsBox.Text.Trim();
        _app.Config.Tv.ExePath = TvPathBox.Text.Trim();
        _app.Config.Tv.Arguments = TvArgsBox.Text.Trim();
        _app.Config.YouTube.ExePath = YouTubePathBox.Text.Trim();
        _app.Config.YouTube.Arguments = YouTubeArgsBox.Text.Trim();
        _app.Config.Epg.FeedUrl = _epgFeedUrl;
        _app.Config.Epg.ChannelId = _epgChannelId;
        _app.Config.Epg.ChannelName = _epgChannelName;
        _app.Config.Epg.XtreamServerUrl = EpgXtreamServerBox.Text.Trim();
        _app.Config.Epg.XtreamUsername = EpgXtreamUserBox.Text.Trim();
        _app.Config.Epg.XtreamPassword = EpgXtreamPasswordBox.Password;
        _app.Config.Epg.XtreamChannelName = string.IsNullOrWhiteSpace(EpgXtreamChannelBox.Text)
            ? _epgChannelName : EpgXtreamChannelBox.Text.Trim();
        _app.Config.FullscreenDashboard = FullscreenCheck.IsChecked == true;
        _app.Config.ScalingMode = ((ComboBoxItem?)ScalingCombo.SelectedItem)?.Tag as string ?? "System";
        _app.Config.ReturnHomeShortcut = string.IsNullOrWhiteSpace(HotkeyBox.Text)
            ? "Ctrl+Alt+Home" : HotkeyBox.Text.Trim();

        try
        {
            _app.ConfigService.Save(_app.Config);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save config", ex);
            ErrorText.Text = "Could not save settings. See the log in %LOCALAPPDATA%\\CouchHub\\logs.";
            return;
        }

        if (!_app.Startup.SetEnabled(StartupCheck.IsChecked == true))
            ErrorText.Text = "Settings saved, but the startup shortcut could not be changed. " +
                             "RedSilk will keep its previous startup setting.";

        DialogResult = true;
        Close();
    }

    private static bool IsValidHttpUrl(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Exit RedSilk completely?", "RedSilk",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            Application.Current.Shutdown();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }
}
