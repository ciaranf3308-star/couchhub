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
        foreach (ComboBoxItem item in ThemeCombo.Items)
        {
            if ((item.Tag as string) == _app.Config.Theme)
                ThemeCombo.SelectedItem = item;
        }
        ThemeCombo.SelectedItem ??= ThemeCombo.Items[0];

        _controllerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _controllerTimer.Tick += (_, _) => RefreshControllerStatus();
        RefreshControllerStatus();
        Loaded += (_, _) => _controllerTimer.Start();
        Closed += (_, _) =>
        {
            _controllerTimer.Stop();
            UnsubscribeGamepad();
        };

        SubscribeGamepad();
        GamingPathBox.Focus();
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
              "Connect an Xbox controller and it will work without restarting CouchHub.";
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

        _app.Config.Gaming.ExePath = GamingPathBox.Text.Trim();
        _app.Config.Gaming.Arguments = GamingArgsBox.Text.Trim();
        _app.Config.Tv.ExePath = TvPathBox.Text.Trim();
        _app.Config.Tv.Arguments = TvArgsBox.Text.Trim();
        _app.Config.YouTube.ExePath = YouTubePathBox.Text.Trim();
        _app.Config.YouTube.Arguments = YouTubeArgsBox.Text.Trim();
        _app.Config.FullscreenDashboard = FullscreenCheck.IsChecked == true;
        _app.Config.ScalingMode = ((ComboBoxItem?)ScalingCombo.SelectedItem)?.Tag as string ?? "System";
        string theme = ((ComboBoxItem?)ThemeCombo.SelectedItem)?.Tag as string ?? "Bedroom";
        _app.Config.Theme = theme == "Hotel" ? "Hotel" : "Bedroom";
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
                             "CouchHub will keep its previous startup setting.";

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Exit CouchHub completely?", "CouchHub",
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
