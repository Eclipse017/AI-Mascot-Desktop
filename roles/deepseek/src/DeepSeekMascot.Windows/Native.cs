using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using AIMascot.Core;

namespace AIMascot.DeepSeek;

internal static class Native
{
    private const int GwlExStyle = -20;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out RECT value, int size);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO
    {
        public int Size; public RECT Monitor, Work; public uint Flags;
    }

    internal static void Configure(Window window, bool inspect, Action? displayChanged = null)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, (nint)(style | 0x08000000L | (inspect ? 0L : 0x00000080L)));
        HwndSource.FromHwnd(hwnd)?.AddHook((nint h, int msg, nint w, nint l, ref bool handled) =>
        {
            if (msg == 0x0021) { handled = true; return (nint)3; } // MA_NOACTIVATE
            if (msg is 0x02E0 or 0x007E) window.Dispatcher.BeginInvoke(() => displayChanged?.Invoke());
            return nint.Zero;
        });
    }

    internal static void RaiseWithoutActivation(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != 0) SetWindowPos(hwnd, (nint)(-1), 0, 0, 0, 0, 0x0013); // TOPMOST | NOMOVE | NOSIZE | NOACTIVATE
    }

    internal static void ClampToScreen(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero || !GetWindowRect(hwnd, out var r)) return;
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return;
        int x = Math.Clamp(r.Left, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - (r.Right - r.Left)));
        int y = Math.Clamp(r.Top, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - (r.Bottom - r.Top)));
        if (x != r.Left || y != r.Top)
            SetWindowPos(hwnd, nint.Zero, x, y, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
    }

    internal static object? FullscreenDetails { get; private set; }
    internal static bool FullscreenOnPetMonitor(Window pet)
    {
        FullscreenDetails = null;
        var foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == GetShellWindow() || IsIconic(foreground) || !IsWindowVisible(foreground)) return false;
        GetWindowThreadProcessId(foreground, out uint processId);
        if (processId == Environment.ProcessId) return false;
        var className = new StringBuilder(128); GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var petHandle = new WindowInteropHelper(pet).EnsureHandle();
        var monitor = MonitorFromWindow(petHandle, 2);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(foreground, out var rect)) return false;
        if (DwmGetWindowAttribute(foreground, 9, out var visibleBounds, Marshal.SizeOf<RECT>()) == 0) rect = visibleBounds;
        long style = GetWindowLongPtr(foreground, -16).ToInt64();
        bool maximized = (style & 0x01000000) != 0;
        bool decorated = (style & 0x00C00000) != 0 || (style & 0x00040000) != 0;
        bool result = FullscreenPolicy.IsFullscreen(new(rect.Left, rect.Top, rect.Right, rect.Bottom),
            new(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom), maximized, decorated);
        FullscreenDetails = new { WindowClass = className.ToString(), Maximized = maximized, Decorated = decorated, Result = result,
            Bounds = new {rect.Left, rect.Top, rect.Right, rect.Bottom}, Monitor = new {info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom} };
        return result;
    }

    internal sealed record HitResult(double LocalX, double LocalY, double ScreenX, double ScreenY, bool PetHit, bool TargetFound);
    internal static HitResult HitProbe(Window window, Point local)
    {
        var screen = window.PointToScreen(local);
        var found = WindowFromPoint(new POINT { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) });
        return new(local.X, local.Y, screen.X, screen.Y, found == new WindowInteropHelper(window).Handle, found != 0);
    }
}
