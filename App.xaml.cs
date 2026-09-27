using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using WinCenter.Core;
using WinCenter.Themes;
using WinCenter.Tray;
using WinCenter.ViewModels;

namespace WinCenter;

public partial class App : Application
{
    private SingleInstance? _instance;
    private MainViewModel? _vm;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool startInTray = e.Args.Any(a => string.Equals(a, AutostartService.TrayArgument, StringComparison.OrdinalIgnoreCase));

        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            if (!startInTray)
            {
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                _instance.SignalFirstInstance();
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        ThemeAccent.Attach(this);

        // Respect "Animation effects" off in Windows settings; template content resolves this key lazily.
        if (!SystemParameters.ClientAreaAnimation)
            Resources["MotionFast"] = new Duration(TimeSpan.Zero);

        var store = new SettingsStore();
        _vm = new MainViewModel(store.Load(), store);
        _vm.OpenSettingsRequested += ShowSettings;
        _vm.HideRequested += () => _window?.Hide();
        _vm.ExitRequested += ExitApp;

        _tray = new TrayIcon(_vm);

        try
        {
            AutostartService.RefreshPath();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            ErrorLog.Write(ex);
        }

        _instance.ListenForSignals(() => Dispatcher.BeginInvoke(ShowSettings));

        if (!startInTray)
            ShowSettings();

        if (!_vm.WelcomeShown)
        {
            _tray.ShowWelcome();
            _vm.MarkWelcomeShown();
        }
    }

    private void ShowSettings()
    {
        if (_vm is null)
            return;
        if (_window is null)
        {
            _window = new MainWindow(_vm);
            _window.Closing += OnWindowClosing;
        }
        if (!_window.IsVisible)
            _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
            return;
        e.Cancel = true;
        _window?.Hide();
    }

    private void ExitApp()
    {
        _exiting = true;
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _vm?.Dispose();
        ThemeAccent.Detach();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write(e.Exception);
        e.Handled = true;
    }
}
