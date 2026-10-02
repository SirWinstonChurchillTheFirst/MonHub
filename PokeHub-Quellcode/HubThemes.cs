using System.Windows;
using RandoApp;

namespace PokeHub;

/// <summary>A MonHub look. Colours here are only for the preview tiles in "Optionen" – the theme itself is Themes\{Id}.xaml.</summary>
public record HubTheme(string Id, string Name, string Tagline, bool Light, SpriteTint Tint,
    string PreviewRail, string PreviewBackground, string PreviewSurface, string PreviewAccent, string PreviewInk)
{
    /// <summary>The menu sits on the right (Game Boy: the START menu).</summary>
    public bool RailRight { get; init; }
}

/// <summary>
/// Themes are resource dictionaries (Themes\*.xaml) on top of Themes\Base.xaml: colours, shapes, borders, shadows, fonts,
/// cursor, decoration and – where a theme needs it – its own control templates. Switching swaps one dictionary,
/// so WPF re-evaluates the resources once.
/// </summary>
public static class HubThemes
{
    public const string Default = "pokedex";

    public static readonly HubTheme[] All =
    [
        new("pokedex", Txt.L("Rot", "Red"), Txt.L("Rotes Gehäuse, Display-Scheibe, Status-LEDs", "Red casing, display lens, status LEDs"), false, SpriteTint.None,
            "#C8202A", "#141A22", "#1E2733", "#46C3F0", "#E6EEF4"),
        new("gameboy", "Retro", Txt.L("Vier Grüntöne, Pixelschrift, START-Menü mit ▶", "Four greens, pixel font, START menu with ▶"), true, SpriteTint.GameBoy,
            "#9BBC0F", "#8BAC0F", "#9BBC0F", "#0F380F", "#0F380F") { RailRight = true },
        new("center", Txt.L("Rosa", "Pink"), Txt.L("Hell und freundlich, Heilstation im Menü", "Bright and friendly, a healing station in the menu"), true, SpriteTint.None,
            "#FFFFFF", "#FBF1EA", "#FFFFFF", "#E0485C", "#3E2A2F"),
        new("cgear", "Neon", Txt.L("Nachtblau mit Lichtringen", "Night blue with rings of light"), false, SpriteTint.None,
            "#0B1230", "#070B1F", "#101A3D", "#35D2FF", "#DDE8FF"),
        new("safari", Txt.L("Savanne", "Savanna"), Txt.L("Holzschilder, Gras und Sand", "Wooden signs, grass and sand"), true, SpriteTint.None,
            "#8A5A2E", "#EFE3C2", "#FFF8E6", "#4E9A37", "#3A2A18"),
        new("lavandia", Txt.L("Spuk", "Spooky"), Txt.L("Nacht im Geisterturm, Kerzen und ein Nebulak", "Night in the ghost tower, candles and a Gastly"), false, SpriteTint.None,
            "#0E0819", "#150D26", "#1E1435", "#B28DFF", "#E6DCFF"),
        new("schlicht", Txt.L("Schlicht", "Plain"), Txt.L("Ruhig und modern, ohne Deko", "Calm and modern, no decoration"), true, SpriteTint.None,
            "#FFFFFF", "#F4F4F2", "#FFFFFF", "#D93A26", "#1E1E1E"),
    ];

    public static HubTheme Current { get; private set; } = All[0];

    public static event Action? Changed;

    static ResourceDictionary? _slot;

    public static void Apply(string? id)
    {
        var theme = All.FirstOrDefault(t => t.Id == id) ?? All[0];
        var app = Application.Current;
#pragma warning disable WPF0001 // Fluent ThemeMode: light or dark standard controls to match the theme
        app.ThemeMode = theme.Light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{theme.Id}.xaml", UriKind.Absolute) };
        var merged = app.Resources.MergedDictionaries;
        if (_slot != null && merged.IndexOf(_slot) is >= 0 and var at) merged[at] = dict;
        else merged.Add(dict);
        _slot = dict;
        Sprites.SetTint(theme.Tint);
        Current = theme;
        Changed?.Invoke();
    }
}

/// <summary>MonHub's motion setting on top of the shared sprite rule.</summary>
public static class HubMotion
{
    public static MotionLevel Level { get; private set; } = MotionLevel.All;

    public static event Action? Changed;

    public static void Apply(MotionLevel level)
    {
        Level = level;
        Motion.Enabled = level != MotionLevel.Off;
        Changed?.Invoke();
    }

    /// <summary>Theme details (LEDs, rings, cursor) and team sprites only move at the full level.</summary>
    public static bool Decorations => Level == MotionLevel.All;
}
