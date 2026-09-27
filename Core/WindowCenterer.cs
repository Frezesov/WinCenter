using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinCenter.Core;

internal readonly record struct CenterOptions(
    bool CustomWidth, int WidthPercent,
    bool CustomHeight, int HeightPercent,
    bool ForceResize, bool Animate);

internal static class WindowCenterer
{
    // Class-name fragments of shell surfaces, menus and tooltips that must never be moved.
    // TopLevelWindowForOverflowXamlIsland is the "Show hidden icons" panel of the Windows 11 tray.
    private static readonly string[] ExcludedClassParts =
    [
        "CoreWindow", "WorkerW", "Flyout", "DV2ControlHost", "NotifyIcon", "NativeHWNDHost", "Popup", "Progman",
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "TaskListThumbnailWnd", "XamlExplorerHostIslandWindow",
        "ForegroundStaging", "MultitaskingViewFrame", "#32768", "tooltips_class32",
        "TopLevelWindowForOverflowXamlIsland", "SystemTray_Main", "EdgeUiInputTopWndClass", "DummyDWMListenerWindow",
        "ThumbnailDeviceHelperWnd", "TabletModeCoverWindow", "ApplicationManager_DesktopShellWindow",
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

            Place(hwnd, work, rect, frame, w, h, resize, o.Animate);

            // Windows with min/max size constraints may reject the requested size; re-center on the real one.
            if (resize && TryGetBounds(hwnd, out rect, out frame) && (frame.Width != w || frame.Height != h))
                Place(hwnd, work, rect, frame, frame.Width, frame.Height, resize: false, animate: false);

            return true;
        }
    }

    private static void Place(IntPtr hwnd, Native.RECT work, Native.RECT rect, Native.RECT frame, int w, int h, bool resize, bool animate)
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

        var target = new Native.RECT { Left = left - ml, Top = top - mt, Right = left + w + mr, Bottom = top + h + mb };
        if (animate)
            Glide(hwnd, rect, target, flags);
        Native.SetWindowPos(hwnd, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, flags);
    }

    private const double GlideMs = 200;

    // Moves the window toward the target once per compositor frame, fast at first and settling at the end
    // like Windows' own window animations. The caller places the window exactly afterwards.
    private static void Glide(IntPtr hwnd, Native.RECT from, Native.RECT to, uint flags)
    {
        int dx = to.Left - from.Left, dy = to.Top - from.Top;
        int dw = to.Width - from.Width, dh = to.Height - from.Height;
        if ((flags & Native.SWP_NOSIZE) != 0)
            dw = dh = 0;
        if (Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Max(Math.Abs(dw), Math.Abs(dh))) < 4)
            return;
        // A hung window would block every step for seconds.
        if (Native.IsHungAppWindow(hwnd))
            return;

        var clock = Stopwatch.StartNew();
        while (true)
        {
            double t = clock.Elapsed.TotalMilliseconds / GlideMs;
            if (t >= 1)
                return;
            double k = 1 - Math.Pow(1 - t, 3);
            Native.SetWindowPos(hwnd, IntPtr.Zero,
                from.Left + (int)Math.Round(dx * k), from.Top + (int)Math.Round(dy * k),
                from.Width + (int)Math.Round(dw * k), from.Height + (int)Math.Round(dh * k), flags);
            if (Native.DwmFlush() != 0)
                Thread.Sleep(10);
        }
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
