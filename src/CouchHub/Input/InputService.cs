using CouchHub.Services;

namespace CouchHub.Input;

/// <summary>Direction of a navigation step.</summary>
public enum NavDirection { Up, Down, Left, Right }

/// <summary>
/// Merges gamepad and keyboard intent into one set of events, so the
/// dashboard never has to know which device produced a command.
/// Gamepad polling runs on a background thread; all events are raised
/// on that thread and the UI marshals them to the Dispatcher.
/// </summary>
public sealed class InputService : IDisposable
{
    public event Action<NavDirection>? Navigate;
    public event Action? Select;
    public event Action? Back;
    public event Action? OpenSettings;
    /// <summary>Y button: open the new-episodes view (v0.7.0).</summary>
    public event Action? OpenNotifications;
    /// <summary>View + Menu held for 2 seconds: return to CouchHub.</summary>
    public event Action? ReturnHomeRequested;
    /// <summary>Raised when controller presence changes (for the settings screen).</summary>
    public event Action<bool>? ControllerPresenceChanged;

    private readonly XInputService _xinput;
    private Thread? _pollThread;
    private volatile bool _running;
    private volatile bool _paused; // true while a launched app owns the foreground

    // Edge detection + repeat timing for the stick / D-pad.
    private ushort _prevButtons;
    private short _prevStickX;
    private short _prevStickY;
    private NavDirection? _heldDirection;
    private DateTime _heldSince;
    private DateTime _lastRepeat;
    private DateTime? _viewMenuHoldStart;
    private bool _wasConnected;

    // Stick: first step immediately, then repeat every 140 ms after 350 ms.
    private static readonly TimeSpan StickInitialDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan StickRepeatInterval = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan ViewMenuHoldDuration = TimeSpan.FromSeconds(2);

    /// <summary>True when any controller is connected right now.</summary>
    public bool IsControllerConnected => _xinput.IsControllerConnected();
    public bool IsXInputAvailable => _xinput.IsAvailable;
    public string? XInputDll => _xinput.LoadedDll;

    public InputService(XInputService xinput) => _xinput = xinput;

    /// <summary>Start the background poll loop. Cheap when idle: sleeps between polls.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "CouchHub-Input",
            Priority = ThreadPriority.BelowNormal
        };
        _pollThread.Start();
    }

    /// <summary>
    /// Pause gamepad navigation while an external app is in front, but
    /// keep watching for View+Menu so return-home still works.
    /// Polling slows to 200 ms while paused.
    /// </summary>
    public void SetPaused(bool paused) => _paused = paused;

    public void Stop()
    {
        _running = false;
        _pollThread?.Join(TimeSpan.FromSeconds(1));
        _pollThread = null;
    }

    private void PollLoop()
    {
        while (_running)
        {
            try
            {
                // No busy loop: 50 ms while the dashboard is active,
                // 200 ms while another app owns the foreground.
                Thread.Sleep(_paused ? 200 : 50);
                if (!_xinput.IsAvailable) continue;

                bool connected = _xinput.TryGetState(out var state);
                if (connected != _wasConnected)
                {
                    _wasConnected = connected;
                    ControllerPresenceChanged?.Invoke(connected);
                    if (!connected) ResetHeldState();
                }
                if (!connected) continue;

                HandleButtons(state.Buttons);
                if (!_paused) HandleStick(state.ThumbLX, state.ThumbLY);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Input poll error (ignored): {ex.GetType().Name}");
                Thread.Sleep(500);
            }
        }
    }

    private void ResetHeldState()
    {
        _prevButtons = 0;
        _heldDirection = null;
        _viewMenuHoldStart = null;
    }

    private void HandleButtons(ushort buttons)
    {
        // View (Back) + Menu (Start) held together for 2 s -> return home.
        // This is deliberately a long, two-button hold so ordinary
        // navigation can never trigger it by accident.
        bool viewMenu = (buttons & XInputService.Back) != 0 && (buttons & XInputService.Start) != 0;
        if (viewMenu)
        {
            _viewMenuHoldStart ??= DateTime.UtcNow;
            if (DateTime.UtcNow - _viewMenuHoldStart.Value >= ViewMenuHoldDuration)
            {
                _viewMenuHoldStart = null;
                ReturnHomeRequested?.Invoke();
            }
        }
        else
        {
            _viewMenuHoldStart = null;
        }

        // Always track the edge state, even while paused, so a button held
        // down while another app is in front cannot fire on return home.
        ushort pressed = (ushort)(buttons & ~_prevButtons); // rising edge only
        _prevButtons = buttons;
        if (_paused) return; // navigation keys are ignored while paused
        if (pressed == 0) return;

        if ((pressed & XInputService.DPadUp) != 0) Navigate?.Invoke(NavDirection.Up);
        if ((pressed & XInputService.DPadDown) != 0) Navigate?.Invoke(NavDirection.Down);
        if ((pressed & XInputService.DPadLeft) != 0) Navigate?.Invoke(NavDirection.Left);
        if ((pressed & XInputService.DPadRight) != 0) Navigate?.Invoke(NavDirection.Right);
        if ((pressed & XInputService.A) != 0) Select?.Invoke();
        if ((pressed & XInputService.B) != 0) Back?.Invoke();
        // Y opens the new-episodes view (v0.7.0); X is unassigned.
        if ((pressed & XInputService.Y) != 0) OpenNotifications?.Invoke();
        // Menu (Start) alone opens settings; View (Back) alone does nothing.
        if ((pressed & XInputService.Start) != 0 && (buttons & XInputService.Back) == 0)
            OpenSettings?.Invoke();
    }

    private void HandleStick(short lx, short ly)
    {
        NavDirection? dir = null;
        // Left stick only; dead zone filters drift. The dominant axis wins.
        if (Math.Abs(lx) > XInputService.ThumbDeadZone && Math.Abs(lx) >= Math.Abs(ly))
            dir = lx > 0 ? NavDirection.Right : NavDirection.Left;
        else if (Math.Abs(ly) > XInputService.ThumbDeadZone)
            dir = ly > 0 ? NavDirection.Up : NavDirection.Down; // Y is up-positive in XInput

        DateTime now = DateTime.UtcNow;
        if (dir is null)
        {
            _heldDirection = null;
            return;
        }
        if (dir != _heldDirection)
        {
            _heldDirection = dir;
            _heldSince = now;
            _lastRepeat = now;
            Navigate?.Invoke(dir.Value);
            return;
        }
        // Held in one direction: repeat after the initial delay.
        if (now - _heldSince >= StickInitialDelay && now - _lastRepeat >= StickRepeatInterval)
        {
            _lastRepeat = now;
            Navigate?.Invoke(dir.Value);
        }
    }

    public void Dispose()
    {
        Stop();
        _xinput.Dispose();
    }
}
