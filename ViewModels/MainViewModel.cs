using System.Windows.Threading;
using WinCenter.Core;

namespace WinCenter.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly HotkeyService _hotkeys;
    private readonly ForegroundWatcher _watcher;
    private readonly AutoCenterService _auto;
    private readonly DispatcherTimer _saveTimer;
    private bool _autostart;

    public event Action? OpenSettingsRequested;
    public event Action? HideRequested;
    public event Action? ExitRequested;

    internal MainViewModel(AppSettings settings, SettingsStore store)
    {
        _settings = settings;
        _store = store;
        settings.WidthPercent = Math.Clamp(settings.WidthPercent, 10, 100);
        settings.HeightPercent = Math.Clamp(settings.HeightPercent, 10, 100);
        _autostart = AutostartService.IsEnabled;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _store.Save(_settings);
        };

        _hotkeys = new HotkeyService();
        _hotkeys.Pressed += () => CenterAsync(Native.GetForegroundWindow());

        _auto = new AutoCenterService(() => CurrentOptions)
        {
            OnlyNewWindows = settings.OnlyNewWindows,
            SmartFilter = settings.SmartFilter,
            FastReaction = settings.FastReaction,
        };

        _watcher = new ForegroundWatcher();
        _watcher.ForegroundChanged += _auto.OnForegroundChanged;
        _watcher.Start();

        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke());
        HideToTrayCommand = new RelayCommand(() => HideRequested?.Invoke());
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke());

        ApplyHotkey();
        _auto.SetActive(Enabled && AutoEnabled);
    }

    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand HideToTrayCommand { get; }
    public RelayCommand ExitCommand { get; }

    public bool Enabled
    {
        get => _settings.Enabled;
        set
        {
            if (!Update(_settings.Enabled, value, v => _settings.Enabled = v))
                return;
            ApplyHotkey();
            _auto.SetActive(value && AutoEnabled);
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusDetails));
        }
    }

    public bool HotkeyEnabled
    {
        get => _settings.HotkeyEnabled;
        set
        {
            if (!Update(_settings.HotkeyEnabled, value, v => _settings.HotkeyEnabled = v))
                return;
            ApplyHotkey();
            OnPropertyChanged(nameof(StatusDetails));
        }
    }

    public Hotkey Hotkey
    {
        get => new(_settings.HotkeyModifiers, _settings.HotkeyKey);
        set
        {
            if (Hotkey == value)
                return;
            _settings.HotkeyModifiers = value.Modifiers;
            _settings.HotkeyKey = value.Key;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyText));
            OnPropertyChanged(nameof(StatusDetails));
            ScheduleSave();
            ApplyHotkey();
        }
    }

    public bool IsCapturingHotkey
    {
        get;
        set
        {
            if (Set(ref field, value))
                ApplyHotkey();
        }
    }

    public string? HotkeyError
    {
        get;
        private set => Set(ref field, value);
    }

    public bool AutoEnabled
    {
        get => _settings.AutoEnabled;
        set
        {
            if (!Update(_settings.AutoEnabled, value, v => _settings.AutoEnabled = v))
                return;
            _auto.SetActive(Enabled && value);
            OnPropertyChanged(nameof(StatusDetails));
        }
    }

    public bool OnlyNewWindows
    {
        get => _settings.OnlyNewWindows;
        set
        {
            if (Update(_settings.OnlyNewWindows, value, v => _settings.OnlyNewWindows = v))
                _auto.OnlyNewWindows = value;
        }
    }

    public bool SmartFilter
    {
        get => _settings.SmartFilter;
        set
        {
            if (Update(_settings.SmartFilter, value, v => _settings.SmartFilter = v))
                _auto.SmartFilter = value;
        }
    }

    public bool FastReaction
    {
        get => _settings.FastReaction;
        set
        {
            if (Update(_settings.FastReaction, value, v => _settings.FastReaction = v))
                _auto.FastReaction = value;
        }
    }

    public bool CustomWidth
    {
        get => _settings.CustomWidth;
        set => Update(_settings.CustomWidth, value, v => _settings.CustomWidth = v);
    }

    public int WidthPercent
    {
        get => _settings.WidthPercent;
        set => Update(_settings.WidthPercent, Math.Clamp(value, 10, 100), v => _settings.WidthPercent = v);
    }

    public bool CustomHeight
    {
        get => _settings.CustomHeight;
        set => Update(_settings.CustomHeight, value, v => _settings.CustomHeight = v);
    }

    public int HeightPercent
    {
        get => _settings.HeightPercent;
        set => Update(_settings.HeightPercent, Math.Clamp(value, 10, 100), v => _settings.HeightPercent = v);
    }

    public bool ForceResize
    {
        get => _settings.ForceResize;
        set => Update(_settings.ForceResize, value, v => _settings.ForceResize = v);
    }

    public bool Autostart
    {
        get => _autostart;
        set
        {
            if (_autostart == value)
                return;
            try
            {
                AutostartService.Set(value);
                _autostart = value;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            {
                ErrorLog.Write(ex);
                _autostart = AutostartService.IsEnabled;
            }
            OnPropertyChanged();
        }
    }

    public string StatusText => Enabled ? "Работает" : "Приостановлено";

    public string HotkeyText => Hotkey.ToString();

    public string StatusDetails
    {
        get
        {
            if (!Enabled)
                return "Окна не центрируются, пока программа на паузе";
            var parts = new List<string>(2);
            if (HotkeyEnabled && Hotkey.IsValid)
                parts.Add($"{Hotkey} — центрировать активное окно");
            if (AutoEnabled)
                parts.Add("автоцентрирование включено");
            return parts.Count == 0 ? "Все способы центрирования выключены" : string.Join(" · ", parts);
        }
    }

    internal CenterOptions CurrentOptions => new(CustomWidth, WidthPercent, CustomHeight, HeightPercent, ForceResize);

    /// <summary>The window of another app the user was last in, or zero if it is gone.</summary>
    internal IntPtr LastWindow => WindowCenterer.IsCandidate(_watcher.LastWindow) ? _watcher.LastWindow : IntPtr.Zero;

    internal void CenterWindow(IntPtr hwnd) => CenterAsync(hwnd);

    internal bool WelcomeShown => _settings.WelcomeShown;

    internal void MarkWelcomeShown()
    {
        _settings.WelcomeShown = true;
        _store.Save(_settings);
    }

    private bool Update<T>(T current, T value, Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return false;
        assign(value);
        OnPropertyChanged(name);
        ScheduleSave();
        return true;
    }

    private void ApplyHotkey()
    {
        if (!Enabled || !HotkeyEnabled || IsCapturingHotkey || !Hotkey.IsValid)
        {
            _hotkeys.Unregister();
            HotkeyError = HotkeyEnabled && !Hotkey.IsValid && !IsCapturingHotkey
                ? "Сочетание не задано — нажмите на поле и введите новое"
                : null;
            return;
        }
        HotkeyError = _hotkeys.Register(Hotkey)
            ? null
            : "Сочетание уже занято системой или другой программой — выберите другое";
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void CenterAsync(IntPtr hwnd)
    {
        try
        {
            var options = CurrentOptions;
            await Task.Run(() => WindowCenterer.Center(hwnd, options));
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    public void Dispose()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            _store.Save(_settings);
        }
        _watcher.Dispose();
        _hotkeys.Dispose();
    }
}
