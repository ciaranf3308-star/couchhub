using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CouchHub.Services;

/// <summary>
/// Thin Win32 helpers: foreground/window handling and the global
/// return-home hotkey (RegisterHotKey). No shell changes, no hooks.
/// </summary>
public static class WindowHelper
{
    private const int SW_RESTORE = 9;
    private const int SW_SHOWMAXIMIZED = 3;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    // Hotkey modifier flags for RegisterHotKey.
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    /// <summary>Bring a window to the foreground, restoring it if minimised.</summary>
    public static bool BringToFront(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        try
        {
            if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
            else ShowWindow(hWnd, SW_SHOWMAXIMIZED);
            return SetForegroundWindow(hWnd);
        }
        catch { return false; }
    }

    /// <summary>Best-effort maximise of an external app's main window.</summary>
    public static void MaximiseIfPossible(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        try { ShowWindow(hWnd, SW_SHOWMAXIMIZED); } catch { /* best effort */ }
    }

    /// <summary>True if the given window currently owns the foreground.</summary>
    public static bool IsForeground(IntPtr hWnd) => hWnd != IntPtr.Zero && GetForegroundWindow() == hWnd;

    /// <summary>Parse "Ctrl+Alt+Home" style strings. Unknown input -> Ctrl+Alt+Home.</summary>
    public static (uint Modifiers, uint VirtualKey) ParseHotkey(string? text)
    {
        uint mods = 0;
        uint vk = 0x24; // VK_HOME
        if (string.IsNullOrWhiteSpace(text)) return (MOD_CONTROL | MOD_ALT, vk);

        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            string part = raw.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods |= MOD_CONTROL;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= MOD_ALT;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= MOD_SHIFT;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= MOD_WIN;
            else if (part.Equals("Home", StringComparison.OrdinalIgnoreCase)) vk = 0x24;
            else if (part.Equals("F12", StringComparison.OrdinalIgnoreCase)) vk = 0x7B;
            else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                vk = (uint)char.ToUpperInvariant(part[0]);
        }
        if (mods == 0) mods = MOD_CONTROL | MOD_ALT;
        return (mods, vk);
    }

    public static bool TryRegisterHotkey(Window window, int id, string? hotkeyText)
    {
        try
        {
            var (mods, vk) = ParseHotkey(hotkeyText);
            IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
            return RegisterHotKey(handle, id, mods, vk);
        }
        catch (Exception ex)
        {
            Logger.Warn($"RegisterHotKey failed: {ex.GetType().Name}");
            return false;
        }
    }

    public static void UnregisterHotkey(Window window, int id)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero) UnregisterHotKey(handle, id);
        }
        catch { /* best effort */ }
    }

    /// <summary>Find the main-window handle of a process, if it has one yet.</summary>
    public static IntPtr MainWindowHandle(Process process)
    {
        try { process.Refresh(); return process.MainWindowHandle; }
        catch { return IntPtr.Zero; }
    }

    /// <summary>Keep a window topmost briefly so it is not lost behind a game on return.</summary>
    public static void FlashTopmost(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW); // HWND_NOTOPMOST
        }
        catch { /* best effort */ }
    }
}
