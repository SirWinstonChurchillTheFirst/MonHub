using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;

namespace PokeHub;

/// <summary>
/// MonHub speaks English (the default) or German. Every text is written in both right where it is used –
/// <c>Txt.L("Spiel löschen", "Delete game")</c> in code, <c>{local:L 'Spiel löschen', 'Delete game'}</c> in XAML.
/// The language is fixed for one start (pages are built ahead, tables are filled once); switching restarts MonHub.
/// </summary>
public static class Txt
{
    static bool? _english;

    /// <summary>
    /// Read from the settings file itself, not from <see cref="HubConfig"/>: tables with texts can be filled while the
    /// config is still being loaded.
    /// </summary>
    public static bool English => _english ??= ReadChoice() != "de";

    /// <summary>"en" or "de".</summary>
    public static string Code => English ? "en" : "de";

    /// <summary>Fixes the language for this start (tests and screenshots).</summary>
    public static void Use(bool english) => _english = english;

    public static string L(string de, string en) => English ? en : de;

    /// <summary>The language picked in MonHub, else the one picked in Setup (System\App\install.ini), else none.</summary>
    static string? ReadChoice() => FromSettings() ?? FromSetup();

    static string? FromSettings()
    {
        try
        {
            if (!File.Exists(HubPaths.HubConfig)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(HubPaths.HubConfig));
            return doc.RootElement.TryGetProperty(nameof(HubConfig.Language), out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch
        {
            return null; // unreadable settings: the default
        }
    }

    static string? FromSetup()
    {
        try
        {
            var ini = Path.Combine(HubPaths.AppDir, "install.ini");
            if (!File.Exists(ini)) return null;
            var line = File.ReadLines(ini).FirstOrDefault(l => l.StartsWith("Language=", StringComparison.OrdinalIgnoreCase));
            return line?[9..].Trim().ToLowerInvariant() switch { "german" => "de", "english" => "en", _ => null };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Numbers, dates and sorting follow the language too.</summary>
    public static void ApplyCulture()
    {
        var culture = CultureInfo.GetCultureInfo(English ? "en-US" : "de-DE");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
    }
}

/// <summary>A text in XAML: <c>{local:L 'Deutsch', 'English'}</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public class LExtension(string de, string en) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Txt.L(de, en);
}
