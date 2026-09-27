using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using WinCenter.Controls;
using WinCenter.Core;
using WinCenter.ViewModels;
using Forms = System.Windows.Forms;

namespace WinCenter.Tray;

internal sealed class TrayIcon : IDisposable
{
    private const string AppName = "WinCenter";

    private readonly MainViewModel _vm;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly System.Drawing.Icon _activeIcon;
    private readonly System.Drawing.Icon _pausedIcon;
    private readonly ContextMenu _menu;
    private readonly MenuItem _headerItem;
    private readonly MenuItem _centerItem;
    private Window? _menuHost;
    private IntPtr _menuTarget;

    public TrayIcon(MainViewModel vm)
    {
        _vm = vm;
        var size = Forms.SystemInformation.SmallIconSize;
        _activeIcon = LoadIcon("app.ico", size);
        _pausedIcon = LoadIcon("app-paused.ico", size);

        _notifyIcon = new Forms.NotifyIcon { Visible = false };
        _notifyIcon.MouseUp += OnMouseUp;

        _headerItem = new MenuItem { IsEnabled = false, FontWeight = FontWeights.SemiBold };
        _centerItem = new MenuItem();
        _centerItem.Click += (_, _) => _vm.CenterWindow(_menuTarget);

        _menu = new ContextMenu { DataContext = vm };
        _menu.SetResourceReference(FrameworkElement.StyleProperty, "MenuStyle");
        _menu.Items.Add(_headerItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(CheckItem("Центрирование включено", nameof(MainViewModel.Enabled)));
        _menu.Items.Add(_centerItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(CheckItem("Центрирование по сочетанию клавиш", nameof(MainViewModel.HotkeyEnabled)));
        _menu.Items.Add(CheckItem("Автоматическое центрирование окон", nameof(MainViewModel.AutoEnabled)));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(CheckItem("Запускать вместе с Windows", nameof(MainViewModel.Autostart)));
        _menu.Items.Add(new MenuItem { Header = "Настройки…", FontWeight = FontWeights.SemiBold, Command = vm.OpenSettingsCommand });
        _menu.Items.Add(new Separator());
        _menu.Items.Add(new MenuItem { Header = "Выход", Command = vm.ExitCommand });
        _menu.Closed += (_, _) => _menuHost?.Hide();

        _vm.PropertyChanged += OnViewModelChanged;
        Refresh();
        _notifyIcon.Visible = true;
    }

    public void ShowWelcome() =>
        _notifyIcon.ShowBalloonTip(5000, AppName + " работает в трее",
            $"{_vm.HotkeyText} — центрировать активное окно. Нажмите на значок, чтобы открыть настройки.",
            Forms.ToolTipIcon.None);

    private static MenuItem CheckItem(string header, string path)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, StaysOpenOnClick = false };
        item.SetBinding(MenuItem.IsCheckedProperty, new Binding(path) { Mode = BindingMode.TwoWay });
        return item;
    }

    private static System.Drawing.Icon LoadIcon(string name, System.Drawing.Size size)
    {
        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))
                   ?? throw new InvalidOperationException($"Missing resource {name}");
        using var stream = info.Stream;
        return new System.Drawing.Icon(stream, size);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Enabled) or nameof(MainViewModel.Hotkey)
            or nameof(MainViewModel.HotkeyEnabled) or nameof(MainViewModel.AutoEnabled))
            Refresh();
    }

    private void Refresh()
    {
        _notifyIcon.Icon = _vm.Enabled ? _activeIcon : _pausedIcon;
        var tip = $"{AppName} — {_vm.StatusText.ToLowerInvariant()}";
        if (_vm.Enabled && _vm.HotkeyEnabled && _vm.Hotkey.IsValid)
            tip += Environment.NewLine + _vm.HotkeyText;
        _notifyIcon.Text = tip.Length > 127 ? tip[..127] : tip;
        _headerItem.Header = $"{AppName} · {_vm.StatusText.ToLowerInvariant()}";
        _centerItem.InputGestureText = _vm.HotkeyEnabled && _vm.Hotkey.IsValid ? _vm.HotkeyText : "";
    }

    private void OnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
            _vm.OpenSettingsCommand.Execute(null);
        else if (e.Button == Forms.MouseButtons.Right)
            ShowMenu();
    }

    // A context menu only closes on outside clicks when its app owns the foreground,
    // so an invisible host window is activated first.
    private void ShowMenu()
    {
        // The target is fixed before the menu takes the foreground, and named so it is clear what will move.
        _menuTarget = _vm.LastWindow;
        var name = _menuTarget == IntPtr.Zero ? "" : WindowInfo.GetDisplayName(_menuTarget);
        _centerItem.Header = _menuTarget == IntPtr.Zero
            ? "Нет окна для центрирования"
            : name.Length == 0 ? "Центрировать последнее окно" : $"Центрировать окно «{WindowInfo.Shorten(name, 40)}»";
        _centerItem.IsEnabled = _menuTarget != IntPtr.Zero;

        _menuHost ??= CreateMenuHost();
        _menuHost.Show();
        _menuHost.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(_menuHost).Handle);

        MenuPlacement.OpenAtCursor(_menu, _menuHost);
    }

    private static Window CreateMenuHost() => new()
    {
        Width = 1,
        Height = 1,
        Left = -32000,
        Top = -32000,
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        ShowActivated = true,
        AllowsTransparency = true,
        Background = System.Windows.Media.Brushes.Transparent,
        Topmost = true,
        Title = "",
    };

    public void Dispose()
    {
        _vm.PropertyChanged -= OnViewModelChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _activeIcon.Dispose();
        _pausedIcon.Dispose();
        _menuHost?.Close();
    }
}
