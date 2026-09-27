using System.Collections;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WinCenter.Themes;

// Fluent bakes the Windows accent into ~65 brushes, so overriding SystemAccentColor* has no effect.
// Instead every Fluent brush that uses an accent shade is re-created in the same role from the teal palette.
internal static class ThemeAccent
{
    private static readonly Color Teal = Hex("#0D9488");
    private static readonly Color TealLight1 = Hex("#14B8A6");
    private static readonly Color TealLight2 = Hex("#2DD4BF");
    private static readonly Color TealLight3 = Hex("#5EEAD4");
    private static readonly Color TealDark1 = Hex("#0F766E");
    private static readonly Color TealDark2 = Hex("#115E59");
    private static readonly Color TealDark3 = Hex("#134E4A");

    private static readonly List<object> AppliedKeys = [];
    private static Application? _app;

    public static void Attach(Application app)
    {
        _app = app;
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void Detach() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
            return;
        var dispatcher = _app?.Dispatcher;
        if (dispatcher is null)
            return;
        // Fluent swaps its light/dark dictionary on the same notification; run after it, and once more a bit later.
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Apply);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.ApplicationIdle, (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            Apply();
        }, dispatcher);
        timer.Start();
    }

    public static void Apply()
    {
        if (_app is null)
            return;
        var resources = _app.Resources;
        foreach (var key in AppliedKeys)
            resources.Remove(key);
        AppliedKeys.Clear();

        if (SystemParameters.HighContrast)
            return;

        var map = new Dictionary<Color, Color>();
        map.TryAdd(Rgb(SystemColors.AccentColor), Teal);
        map.TryAdd(Rgb(SystemColors.AccentColorLight1), TealLight1);
        map.TryAdd(Rgb(SystemColors.AccentColorLight2), TealLight2);
        map.TryAdd(Rgb(SystemColors.AccentColorLight3), TealLight3);
        map.TryAdd(Rgb(SystemColors.AccentColorDark1), TealDark1);
        map.TryAdd(Rgb(SystemColors.AccentColorDark2), TealDark2);
        map.TryAdd(Rgb(SystemColors.AccentColorDark3), TealDark3);

        var replacements = new Dictionary<object, object>();
        foreach (var dictionary in resources.MergedDictionaries)
            if (dictionary.Contains("AccentFillColorDefaultBrush"))
                Collect(dictionary, map, replacements);

        foreach (var (key, value) in replacements)
        {
            resources[key] = value;
            AppliedKeys.Add(key);
        }
    }

    private static void Collect(ResourceDictionary dictionary, Dictionary<Color, Color> map, Dictionary<object, object> result)
    {
        foreach (var merged in dictionary.MergedDictionaries)
            Collect(merged, map, result);

        foreach (DictionaryEntry entry in dictionary)
        {
            switch (entry.Value)
            {
                case SolidColorBrush brush when map.TryGetValue(Rgb(brush.Color), out var teal):
                    var replacement = new SolidColorBrush(Color.FromArgb(brush.Color.A, teal.R, teal.G, teal.B));
                    replacement.Freeze();
                    result[entry.Key] = replacement;
                    break;
                case Color color when map.TryGetValue(Rgb(color), out var tealColor):
                    result[entry.Key] = Color.FromArgb(color.A, tealColor.R, tealColor.G, tealColor.B);
                    break;
            }
        }
    }

    private static Color Rgb(Color c) => Color.FromRgb(c.R, c.G, c.B);

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
