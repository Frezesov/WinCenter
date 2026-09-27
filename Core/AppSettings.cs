using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace WinCenter.Core;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;

    public bool HotkeyEnabled { get; set; } = true;
    public ModifierKeys HotkeyModifiers { get; set; } = Hotkey.Default.Modifiers;
    public Key HotkeyKey { get; set; } = Hotkey.Default.Key;

    public bool AutoEnabled { get; set; }
    public bool OnlyNewWindows { get; set; } = true;
    public bool SmartFilter { get; set; } = true;
    public bool FastReaction { get; set; }
    public List<ExcludedApp> ExcludedApps { get; set; } = [];

    public bool CustomWidth { get; set; }
    public int WidthPercent { get; set; } = 80;
    public bool CustomHeight { get; set; }
    public int HeightPercent { get; set; } = 80;
    public bool ForceResize { get; set; }

    public bool WelcomeShown { get; set; }
}

/// <summary>A program that automatic centering leaves alone, matched by its exe file name.</summary>
public sealed class ExcludedApp
{
    public string Exe { get; set; } = "";

    /// <summary>Where the exe was when it was added; only used for its name and icon.</summary>
    public string? Path { get; set; }
}

internal sealed class SettingsStore
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore() => FilePath = ResolvePath();

    public string FilePath { get; }

    // Portable: next to the exe when that folder is writable, otherwise in %APPDATA%.
    private static string ResolvePath()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var portable = Path.Combine(exeDir, FileName);
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinCenter", FileName);

        if (File.Exists(portable)) return portable;
        if (File.Exists(roaming)) return roaming;
        return CanWrite(exeDir) ? portable : roaming;
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
        }
    }
}
