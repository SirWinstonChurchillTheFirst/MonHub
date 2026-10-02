using System.Reflection;

namespace PokeHub;

public enum TourMode
{
    /// <summary>Everything, from the menu to the options (new install, or started under Optionen).</summary>
    Full,
    /// <summary>After an update: only what is new since the version the player last saw.</summary>
    WhatsNew,
}

/// <summary>
/// One stop of Porygon's tour: the page it happens on, the element he points at (first visible of several,
/// "A|B"), what he says, and the version that brought it (for the "what's new" tour after updates).
/// </summary>
public record TourStep(string? Page, string? Target, string Title, string Text, string Since = "1.0", string Icon = "poke-ball");

/// <summary>What Porygon shows, and when it offers the tour.</summary>
public static class Tour
{
    // One area → one short sentence → one action. Kept short on purpose: a tour that feels long gets skipped.
    public static readonly TourStep[] Steps =
    [
        new(null, "Nav", "Das Menü",
            "Hier wechselst du zwischen allen Bereichen von MonHub."),
        new("start", "ContinuePanel|Onboarding", "Weiterspielen",
            "Dein letzter Spielstand – ein Klick, und es geht genau dort weiter."),
        new("games", "GameList|EmptyPanel", "Deine Spiele",
            "Alle Spiele und Runs. Mit ☆ markierst du Favoriten."),
        new("games", "BtnFolders", "Ordner & Import",
            "Hier importierst du ROMs, Spielstände und deine Spieleordner."),
        new("run", "BtnRandomizePlay", "Neuer Run",
            "Links ein Original wählen, hier mischen – das Spiel startet sofort."),
        new("run", "OptionsPanel", "Regeln",
            "Rare Candy, Spoiler und alle Optionen des Randomizers."),
        new("nuzlocke", "RouteList|CmbRun", "Nuzlocke",
            "Pro Route trägst du deinen Fang ein, mit ✗ streichst du ihn."),
        new("pokedex", "DexRows|EmptyPanel", "Dex",
            "Jeder Spielstand hat seinen eigenen – oben wählst du, wessen.", "2.4"),
        new("emulators", "ControlsPanel", "Steuerung",
            "Tasten, Controller und Tempo für alle Emulatoren."),
        new("emulators", "AzaharPanel", "3DS-Spiele",
            "Laufen in Azahar, jeder Run mit eigenem Spielstand. 60 FPS schaltest du hier um.", "2.2"),
        new("options", "ThemeList", "Dein Look",
            "Sieben Looks – such dir einen aus.", "2.2"),
        new("options", "BtnTour", "Tour wiederholen",
            "Diese Tour findest du jederzeit hier.", "2.3"),
    ];

    /// <summary>This MonHub's version (Major.Minor.Build), e.g. 2.3.0.</summary>
    public static Version Current
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    static Version Parse(string text) => Version.TryParse(text, out var v) ? v : new Version(0, 0);

    /// <summary>
    /// The setup writes a new stamp into System\App\install.ini every time it runs, so MonHub knows it was just installed –
    /// also when the same version is installed again. Null while developing (no setup).
    /// </summary>
    static string? InstallStamp()
    {
        try
        {
            var ini = System.IO.Path.Combine(HubPaths.AppDir, "install.ini");
            if (!System.IO.File.Exists(ini)) return null;
            return System.IO.File.ReadLines(ini).Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("Stamp=", StringComparison.OrdinalIgnoreCase))?[6..];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// What to offer on this start: after an install the new parts (update) or the whole tour (new install, same version
    /// again), after "Vielleicht später" the same once more, nothing on a normal restart.
    /// </summary>
    public static (TourMode Mode, List<TourStep> Steps)? Offer()
    {
        var cfg = HubConfig.Current;
        var stamp = InstallStamp();
        bool installed = stamp != null ? stamp != cfg.InstallSeen
                       : string.IsNullOrEmpty(cfg.TourSeen) || Parse(cfg.TourSeen) < Current; // no setup: go by version
        if (!installed && !cfg.TourLater) return null;
        if (string.IsNullOrEmpty(cfg.TourSeen)) return (TourMode.Full, Steps.ToList());
        var seen = Parse(cfg.TourSeen);
        var fresh = Steps.Where(s => Parse(s.Since) > seen).ToList();
        return fresh.Count > 0 ? (TourMode.WhatsNew, fresh) : (TourMode.Full, Steps.ToList());
    }

    public static List<TourStep> For(TourMode mode) => mode == TourMode.Full ? Steps.ToList() : Offer()?.Steps ?? Steps.ToList();

    /// <summary>Shown (or declined): not offered again until the next install.</summary>
    public static void MarkSeen() => Save(later: false);

    /// <summary>"Vielleicht später": asked again on the next start.</summary>
    public static void AskLater() => Save(later: true);

    static void Save(bool later)
    {
        var cfg = HubConfig.Current;
        if (!later) cfg.TourSeen = Current.ToString(3);
        cfg.InstallSeen = InstallStamp() ?? cfg.InstallSeen;
        cfg.TourLater = later;
        try { cfg.Save(); }
        catch { /* not saved: offered once more next time */ }
    }
}
