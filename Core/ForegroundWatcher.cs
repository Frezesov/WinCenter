namespace WinCenter.Core;

internal sealed class ForegroundWatcher : IDisposable
{
    private readonly Native.WinEventDelegate _callback;
    private IntPtr _hook;

    public event Action<IntPtr>? ForegroundChanged;

    // Last activated window of another app — the target of "center active window" from the tray menu,
    // since opening the menu itself moves the foreground to the taskbar and to this app.
    public IntPtr LastWindow { get; private set; }

    public ForegroundWatcher() => _callback = OnWinEvent;

    public void Start()
    {
        var fg = Native.GetForegroundWindow();
        if (IsTrackable(fg))
            LastWindow = fg;

        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _callback, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Native.OBJID_WINDOW || hwnd == IntPtr.Zero)
            return;
        if (IsTrackable(hwnd))
            LastWindow = hwnd;
        ForegroundChanged?.Invoke(hwnd);
    }

    private static bool IsTrackable(IntPtr hwnd) =>
        WindowCenterer.IsCandidate(hwnd) && !WindowCenterer.IsOwnWindow(hwnd);

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
