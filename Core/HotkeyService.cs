using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace WinCenter.Core;

/// <summary>
/// Catches the centering shortcut with a low-level keyboard hook. Low-level hooks run before the shell's own
/// hotkeys, so the shortcut also works on combinations Windows keeps for itself, such as Win + Q, which
/// RegisterHotKey refuses. The hook lives on its own thread with a message loop: a slow UI thread would otherwise
/// delay every keystroke in the system, and Windows silently drops hooks that time out.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private sealed record Shortcut(int Vk, ModifierKeys Modifiers);

    // Marks keystrokes sent by the hook itself so it lets them through.
    private static readonly IntPtr OwnInput = new(0x57434E54);
    // An unassigned key: pressing it while Win or Alt is held keeps the Start menu or the window's menu bar
    // from opening when the modifier is released.
    private const ushort DummyKey = 0xFF;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Native.LowLevelKeyboardProc _proc;
    private Thread? _thread;
    private uint _threadId;
    private volatile Shortcut? _shortcut;

    // Touched by the hook thread only.
    private int _heldKey;

    public event Action? Pressed;

    public HotkeyService() => _proc = HookProc;

    public void Register(Hotkey hotkey)
    {
        if (!hotkey.IsValid)
        {
            Unregister();
            return;
        }
        _shortcut = new((int)hotkey.VirtualKey, hotkey.Modifiers);
        Start();
    }

    public void Unregister()
    {
        _shortcut = null;
        Stop();
    }

    private void Start()
    {
        if (_thread is not null)
            return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "WinCenter keyboard hook" };
        _thread.Start();
        ready.Wait();
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = Native.GetCurrentThreadId();
        // Creates the thread's message queue before anyone may post WM_QUIT to it.
        Native.PeekMessage(out _, IntPtr.Zero, 0, 0, Native.PM_NOREMOVE);
        var hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
            ErrorLog.Write(new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed"));
        ready.Set();

        while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }

        if (hook != IntPtr.Zero)
            Native.UnhookWindowsHookEx(hook);
    }

    private void Stop()
    {
        if (_thread is null)
            return;
        Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
        _heldKey = 0;
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            if (info.dwExtraInfo != OwnInput && Swallow((int)info.vkCode, (int)wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN))
                return 1;
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private bool Swallow(int vk, bool down)
    {
        // Repeats and the release of the key that fired the shortcut go the same way as its press.
        if (vk == _heldKey)
        {
            if (!down)
                _heldKey = 0;
            return true;
        }

        var shortcut = _shortcut;
        if (!down || shortcut is null || shortcut.Vk != vk)
            return false;
        var modifiers = HeldModifiers();
        if (shortcut.Modifiers != modifiers)
            return false;

        _heldKey = vk;
        if ((modifiers & (ModifierKeys.Windows | ModifierKeys.Alt)) != 0)
            PressDummyKey();
        _dispatcher.BeginInvoke(() => Pressed?.Invoke());
        return true;
    }

    private static ModifierKeys HeldModifiers()
    {
        var modifiers = ModifierKeys.None;
        if (Native.IsKeyDown(Native.VK_CONTROL))
            modifiers |= ModifierKeys.Control;
        if (Native.IsKeyDown(Native.VK_MENU))
            modifiers |= ModifierKeys.Alt;
        if (Native.IsKeyDown(Native.VK_SHIFT))
            modifiers |= ModifierKeys.Shift;
        if (Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN))
            modifiers |= ModifierKeys.Windows;
        return modifiers;
    }

    private static void PressDummyKey()
    {
        var inputs = new Native.INPUT[2];
        inputs[0].type = inputs[1].type = Native.INPUT_KEYBOARD;
        inputs[0].u.ki = new Native.KEYBDINPUT { wVk = DummyKey, dwExtraInfo = OwnInput };
        inputs[1].u.ki = new Native.KEYBDINPUT { wVk = DummyKey, dwFlags = Native.KEYEVENTF_KEYUP, dwExtraInfo = OwnInput };
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
    }

    public void Dispose() => Unregister();
}
