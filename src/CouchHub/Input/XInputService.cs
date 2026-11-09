using System.Runtime.InteropServices;
using CouchHub.Services;

namespace CouchHub.Input;

/// <summary>
/// Minimal XInput wrapper, loaded dynamically so the app still runs
/// (keyboard/mouse only) on a machine without XInput.
/// Tries XInput1_4.dll, then XInput1_3.dll, then XInput9_1_0.dll.
/// Only XInputGetState is needed for v0.1; vibration is out of scope.
/// </summary>
public sealed class XInputService : IDisposable
{
    // XInput button flags (XINPUT_GAMEPAD_*).
    public const ushort DPadUp = 0x0001;
    public const ushort DPadDown = 0x0002;
    public const ushort DPadLeft = 0x0004;
    public const ushort DPadRight = 0x0008;
    public const ushort Start = 0x0010;   // "Menu" button on Xbox controllers
    public const ushort Back = 0x0020;    // "View" button on Xbox controllers
    public const ushort A = 0x1000;
    public const ushort B = 0x2000;
    public const ushort X = 0x4000;
    public const ushort Y = 0x8000;

    /// <summary>Standard XInput thumbstick dead zone (7849 of 32767).</summary>
    public const short ThumbDeadZone = 7849;

    [StructLayout(LayoutKind.Sequential)]
    public struct GamepadState
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint XInputGetStateDelegate(uint dwUserIndex, out GamepadState pState);

    private static readonly string[] CandidateDlls =
        { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" };

    private IntPtr _library;
    private XInputGetStateDelegate? _getState;
    private bool _disposed;

    public bool IsAvailable => _getState is not null;
    public string? LoadedDll { get; private set; }

    public XInputService()
    {
        foreach (string dll in CandidateDlls)
        {
            if (NativeLibrary.TryLoad(dll, out IntPtr handle))
            {
                if (NativeLibrary.TryGetExport(handle, "XInputGetState", out IntPtr fn))
                {
                    _library = handle;
                    LoadedDll = dll;
                    _getState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(fn);
                    Logger.Info($"XInput loaded: {dll}");
                    return;
                }
                NativeLibrary.Free(handle);
            }
        }
        Logger.Warn("XInput not available; controller input disabled, keyboard/mouse still work.");
    }

    /// <summary>
    /// Poll controller 0 only (the first connected Xbox controller).
    /// Returns false when no controller is present. ERROR_SUCCESS = 0.
    /// </summary>
    public bool TryGetState(out GamepadState state)
    {
        state = default;
        if (_getState is null) return false;
        try
        {
            return _getState(0, out state) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True if any controller is currently connected (slot 0).</summary>
    public bool IsControllerConnected() => TryGetState(out _);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }
        _getState = null;
    }
}
