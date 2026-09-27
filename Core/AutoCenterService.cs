namespace WindowCentering.Core;

internal sealed class AutoCenterService
{
    private const int StandardDelayMs = 300;
    private const int FastDelayMs = 30;

    private readonly HashSet<IntPtr> _known = [];
    private readonly Func<CenterOptions> _options;
    private bool _onlyNewWindows = true;

    public AutoCenterService(Func<CenterOptions> options) => _options = options;

    public bool Active { get; private set; }
    public bool SmartFilter { get; set; } = true;
    public bool FastReaction { get; set; }

    public bool OnlyNewWindows
    {
        get => _onlyNewWindows;
        set
        {
            if (_onlyNewWindows == value)
                return;
            _onlyNewWindows = value;
            ResetKnownWindows();
        }
    }

    public void SetActive(bool active)
    {
        if (Active == active)
            return;
        Active = active;
        ResetKnownWindows();
    }

    // Windows that already exist when the mode is turned on are not "new" and must not jump around.
    private void ResetKnownWindows()
    {
        _known.Clear();
        if (!Active || !_onlyNewWindows)
            return;
        Native.EnumWindows((h, _) =>
        {
            if (Native.IsWindowVisible(h))
                _known.Add(h);
            return true;
        }, IntPtr.Zero);
    }

    public void OnForegroundChanged(IntPtr hwnd)
    {
        if (!Active)
            return;
        if (_onlyNewWindows)
        {
            if (_known.Count > 512)
                _known.RemoveWhere(h => !Native.IsWindow(h));
            if (!_known.Add(hwnd))
                return;
        }
        _ = CenterLaterAsync(hwnd);
    }

    private async Task CenterLaterAsync(IntPtr hwnd)
    {
        try
        {
            await Task.Delay(FastReaction ? FastDelayMs : StandardDelayMs);
            if (!Active)
                return;

            var options = _options();
            bool smart = SmartFilter;
            await Task.Run(() =>
            {
                if (!WindowCenterer.IsCandidate(hwnd) || WindowCenterer.IsToolWindow(hwnd))
                    return;
                if (smart && !WindowCenterer.PassesSmartFilter(hwnd))
                    return;
                WindowCenterer.Center(hwnd, options);
            });
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }
}
