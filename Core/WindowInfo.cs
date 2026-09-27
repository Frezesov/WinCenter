using System.Diagnostics;
using System.IO;

namespace WinCenter.Core;

internal static class WindowInfo
{
    public static string GetTitle(IntPtr hwnd) => Native.GetWindowText(hwnd).Trim();

    public static string? GetProcessPath(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == 0 ? null : Native.GetProcessPath(pid);
    }

    /// <summary>File name of the window's executable in lower case, e.g. "notepad.exe".</summary>
    public static string? GetExeName(IntPtr hwnd) =>
        GetProcessPath(hwnd) is { } path ? Path.GetFileName(path).ToLowerInvariant() : null;

    /// <summary>Human-readable program name: the file description, or the exe name without extension.</summary>
    public static string GetProgramName(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description))
                return description;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>What the tray menu calls a window: its title, or its program when the title is empty.</summary>
    public static string GetDisplayName(IntPtr hwnd)
    {
        var title = GetTitle(hwnd);
        if (title.Length == 0 && GetProcessPath(hwnd) is { } path)
            title = GetProgramName(path);
        return title;
    }

    public static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
}
