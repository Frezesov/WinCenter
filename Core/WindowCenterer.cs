using System.Runtime.InteropServices;

namespace WinCenter.Core;

internal readonly record struct CenterOptions(
    bool CustomWidth, int WidthPercent,
    bool CustomHeight, int HeightPercent,
    bool ForceResize);

internal static class WindowCenterer
{
    // Class-name fragments of shell surfaces, menus and tooltips that must never be moved.
    private static readonly string[] ExcludedClassParts =
    [
        "CoreWindow", "WorkerW", "Flyout", "DV2ControlHost", "NotifyIcon", "NativeHWNDHost", "Popup", "Progman",
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "TaskListThumbnailWnd", "XamlExplorerHostIslandWindow",
        "ForegroundStaging", "MultitaskingViewFrame", "#32768", "tooltips_class32",
    ];

    private static readonly object Gate = new();
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    public static bool IsCandidate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
            return false;
        if ((Native.GetStyle(hwnd) & Native.WS_CHILD) != 0)
            return false;
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        var cls = Native.GetClassName(hwnd);
        foreach (var part in ExcludedClassParts)
            if (cls.Contains(part, StringComparison.Ordinal))
                return false;
        return true;
    }

    public static bool IsOwnWindow(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == OwnProcessId;
    }

    public static bool IsToolWindow(IntPtr hwnd) =>
        (Native.GetExStyle(hwnd) & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE)) != 0;

    // Skips maximized windows and popups without a system menu or border (menus, flyouts, splash screens).
    public static bool PassesSmartFilter(IntPtr hwnd)
    {
        var style = Native.GetStyle(hwnd);
        if ((style & Native.WS_MAXIMIZE) != 0)
            return false;
        if ((style & Native.WS_POPUP) != 0 && ((style & Native.WS_SYSMENU) == 0 || (style & Native.WS_BORDER) == 0))
            return false;
        return true;
    }

    public static bool Center(IntPtr hwnd, CenterOptions o)
    {
        lock (Gate)
        {
            if (!IsCandidate(hwnd))
                return false;

            var style = Native.GetStyle(hwnd);
            if ((style & Native.WS_MAXIMIZE) != 0)
            {
                if (!(o.CustomWidth && o.CustomHeight))
                    return false;
                Native.ShowWindow(hwnd, Native.SW_RESTORE);
                style = Native.GetStyle(hwnd);
            }

            var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref info))
                return false;
            var work = info.rcWork;

            if (!TryGetBounds(hwnd, out var rect, out var frame))
                return false;

            bool resizable = (style & Native.WS_THICKFRAME) != 0 && (style & Native.WS_MAXIMIZEBOX) != 0;
            bool canResize = resizable || o.ForceResize;
            int w = frame.Width, h = frame.Height;
            bool resize = false;
            if (o.CustomWidth && canResize)
            {
                w = Percent(work.Width, o.WidthPercent);
                resize = true;
            }
            if (o.CustomHeight && canResize)
            {
                h = Percent(work.Height, o.HeightPercent);
                resize = true;
            }

            Place(hwnd, work, rect, frame, w, h, resize);

            // Windows with min/max size constraints may reject the requested size; re-center on the real one.
            if (resize && TryGetBounds(hwnd, out rect, out frame) && (frame.Width != w || frame.Height != h))
                Place(hwnd, work, rect, frame, frame.Width, frame.Height, resize: false);

            return true;
        }
    }

    private static void Place(IntPtr hwnd, Native.RECT work, Native.RECT rect, Native.RECT frame, int w, int h, bool resize)
    {
        int left = work.Left + (work.Width - w) / 2;
        int top = h > work.Height ? work.Top : work.Top + (work.Height - h) / 2;

        // Invisible resize borders: difference between the window rect and the visible DWM frame.
        int ml = frame.Left - rect.Left;
        int mt = frame.Top - rect.Top;
        int mr = rect.Right - frame.Right;
        int mb = rect.Bottom - frame.Bottom;

        uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
        if (!resize)
            flags |= Native.SWP_NOSIZE;

        Native.SetWindowPos(hwnd, IntPtr.Zero, left - ml, top - mt, w + ml + mr, h + mt + mb, flags);
    }

    private static bool TryGetBounds(IntPtr hwnd, out Native.RECT rect, out Native.RECT frame)
    {
        if (!Native.GetWindowRect(hwnd, out rect))
        {
            frame = default;
            return false;
        }
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out frame, Marshal.SizeOf<Native.RECT>()) != 0
            || frame.Width <= 0 || frame.Height <= 0)
            frame = rect;
        return true;
    }

    private static int Percent(int size, int percent) =>
        (int)Math.Round(size * Math.Clamp(percent, 10, 100) / 100.0);
}
