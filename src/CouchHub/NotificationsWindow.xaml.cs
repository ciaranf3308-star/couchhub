using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CouchHub.Input;
using CouchHub.Services;

namespace CouchHub;

/// <summary>
/// New-episodes view (v0.7.0): the list behind the TV tile's unread
/// badge. One row per notification — series title, season/episode,
/// episode title, detected date — and picking a row deep-links into
/// RedSilk TV at that series/episode. Mouse, keyboard (Tab / arrows /
/// Enter / Esc) and controller (D-pad + A / B) all work: the window
/// subscribes to the shared InputService itself while open, and the
/// dashboard ignores navigation input meanwhile (see MainWindow's
/// _notificationsOpen guard), so a press can never do two things.
/// The window closes only after a successful deep-link launch; a
/// failed launch leaves it open so the user can try another row.
/// </summary>
public partial class NotificationsWindow : Window
{
    private readonly App _app;
    private readonly Func<SeriesNotification, bool> _onChoose;
    private readonly List<Button> _itemButtons = new();
    private int _focusedIndex = -1;
    private bool _inputSubscribed;

    public NotificationsWindow(IReadOnlyList<SeriesNotification> notifications,
        Func<SeriesNotification, bool> onChoose)
    {
        InitializeComponent();
        _app = (App)Application.Current;
        _onChoose = onChoose;

        SubtitleText.Text = notifications.Count == 1
            ? "From RedSilk TV — 1 new episode. Pick it to open it at that episode."
            : $"From RedSilk TV — {notifications.Count} new episodes. Pick one to open it at that episode.";

        foreach (var notification in notifications)
            ItemsPanel.Children.Add(BuildItemButton(notification));
        // Tabbing onto Close must clear the item focus index, so the
        // controller's A activates Close instead of a stale row.
        CloseButton.GotFocus += (_, _) => _focusedIndex = -1;

        Loaded += (_, _) =>
        {
            // Controller events arrive on the input thread: the named
            // handlers below marshal to the Dispatcher. Named methods
            // (not lambdas) so Closed can unsubscribe the exact
            // delegates that were added.
            _app.Input.Navigate += OnNavigateInput;
            _app.Input.Select += OnSelectInput;
            _app.Input.Back += OnBackInput;
            _inputSubscribed = true;
            FocusFirstItem();
        };
        Closed += (_, _) =>
        {
            if (!_inputSubscribed) return;
            _inputSubscribed = false;
            _app.Input.Navigate -= OnNavigateInput;
            _app.Input.Select -= OnSelectInput;
            _app.Input.Back -= OnBackInput;
        };
    }

    // Named handlers so Closed can unsubscribe the exact delegates
    // that were added. Each marshals to the UI thread: InputService
    // raises its events on the background poll thread.
    private void OnNavigateInput(NavDirection dir) => Dispatcher.BeginInvoke(() => OnNavigate(dir));
    private void OnSelectInput() => Dispatcher.BeginInvoke(ActivateFocused);
    private void OnBackInput() => Dispatcher.BeginInvoke(() => Close());

    private Button BuildItemButton(SeriesNotification notification)
    {
        string meta = notification.Season is not null && notification.Episode is not null
            ? $"Series {notification.Season} · Episode {notification.Episode}"
            : "New episode";
        if (!string.IsNullOrWhiteSpace(notification.EpisodeTitle))
            meta += $" — {notification.EpisodeTitle}";
        string detected = notification.DetectedAtUtc is not null
            ? notification.DetectedAtUtc.Value.ToLocalTime().ToString("d MMM yyyy, HH:mm")
            : string.Empty;

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(notification.SeriesTitle) ? "Unknown series" : notification.SeriesTitle,
            FontSize = 24,
            FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var metaText = new TextBlock
        {
            Text = meta,
            FontSize = 17,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        var detectedText = new TextBlock
        {
            Text = detected,
            FontSize = 15,
            Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 2, 0, 0)
        };
        var textStack = new StackPanel();
        textStack.Children.Add(title);
        textStack.Children.Add(metaText);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(textStack);
        Grid.SetColumn(detectedText, 1);
        grid.Children.Add(detectedText);

        var button = new Button
        {
            Content = grid,
            Style = (Style)FindResource("NotificationItemButtonStyle"),
            Tag = notification,
            Margin = new Thickness(0, 0, 0, 12)
        };
        button.Click += (_, _) => Choose(notification);
        button.GotFocus += (_, _) => _focusedIndex = _itemButtons.IndexOf(button);
        _itemButtons.Add(button);
        return button;
    }

    private void Choose(SeriesNotification notification)
    {
        // Close only on success — a failed launch (TV app missing,
        // Store target, bad id) leaves the list open with the plain
        // error already shown by the caller.
        if (_onChoose(notification))
            Close();
    }

    private void FocusFirstItem()
    {
        if (_itemButtons.Count == 0)
        {
            CloseButton.Focus();
            Keyboard.Focus(CloseButton);
            return;
        }
        _focusedIndex = 0;
        _itemButtons[0].Focus();
        Keyboard.Focus(_itemButtons[0]);
    }

    private void OnNavigate(NavDirection dir)
    {
        if (_itemButtons.Count == 0) return;
        int next = dir switch
        {
            NavDirection.Up => (_focusedIndex <= 0 ? _itemButtons.Count - 1 : _focusedIndex - 1),
            NavDirection.Down => (_focusedIndex < 0 || _focusedIndex >= _itemButtons.Count - 1 ? 0 : _focusedIndex + 1),
            NavDirection.Left => _focusedIndex,
            NavDirection.Right => _focusedIndex,
            _ => _focusedIndex
        };
        _focusedIndex = next;
        _itemButtons[next].Focus();
        Keyboard.Focus(_itemButtons[next]);
    }

    private void ActivateFocused()
    {
        if (_focusedIndex >= 0 && _focusedIndex < _itemButtons.Count &&
            _itemButtons[_focusedIndex].Tag is SeriesNotification notification)
        {
            Choose(notification);
            return;
        }
        if (CloseButton.IsFocused)
            Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: OnNavigate(NavDirection.Up); e.Handled = true; break;
            case Key.Down: OnNavigate(NavDirection.Down); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
        base.OnPreviewKeyDown(e);
    }
}
