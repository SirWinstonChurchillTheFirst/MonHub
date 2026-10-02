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
        new(null, "Nav", Txt.L("Das Menü", "The menu"),
            Txt.L("Hier wechselst du zwischen allen Bereichen von MonHub.", "This is where you switch between all parts of MonHub.")),
        new("start", "ContinuePanel|Onboarding", Txt.L("Weiterspielen", "Continue"),
            Txt.L("Dein letzter Spielstand – ein Klick, und es geht genau dort weiter.", "Your last save – one click and you carry on right there.")),
        new("games", "GameList|EmptyPanel", Txt.L("Deine Spiele", "Your games"),
            Txt.L("Alle Spiele und Runs. Mit ☆ markierst du Favoriten.", "All games and runs. Mark favorites with ☆.")),
        new("games", "BtnFolders", Txt.L("Ordner & Import", "Folders & import"),
            Txt.L("Hier importierst du ROMs, Spielstände und deine Spieleordner.", "Import ROMs, saves and your game folders here.")),
        new("run", "BtnRandomizePlay", Txt.L("Neuer Run", "New run"),
            Txt.L("Links ein Original wählen, hier mischen – das Spiel startet sofort.", "Pick an original on the left, mix it here – the game starts right away.")),
        new("run", "OptionsPanel", Txt.L("Regeln", "Rules"),
            Txt.L("Rare Candy, Spoiler und alle Optionen des Randomizers.", "Rare Candy, spoilers and all of the randomizer's options.")),
        new("nuzlocke", "RouteList|CmbRun", "Nuzlocke",
            Txt.L("Pro Route trägst du deinen Fang ein, mit ✗ streichst du ihn.", "Enter your catch for each route; strike it with ✗.")),
        new("pokedex", "DexRows|EmptyPanel", "Dex",
            Txt.L("Jeder Spielstand hat seinen eigenen – oben wählst du, wessen.", "Every save has its own – choose whose at the top."), "2.4"),
        new("emulators", "ControlsPanel", Txt.L("Steuerung", "Controls"),
            Txt.L("Tasten, Controller und Tempo für alle Emulatoren.", "Keys, controller and speed for all emulators.")),
        new("emulators", "AzaharPanel", Txt.L("3DS-Spiele", "3DS games"),
            Txt.L("Laufen in Azahar, jeder Run mit eigenem Spielstand. 60 FPS schaltest du hier um.", "They run in Azahar, every run with its own save. Switch 60 FPS here."), "2.2"),
        new("options", "ThemeList", Txt.L("Dein Look", "Your look"),
            Txt.L("Sieben Looks – such dir einen aus.", "Seven looks – pick one."), "2.2"),
        new("options", "LanguagePanel", Txt.L("Sprache", "Language"),
            Txt.L("MonHub spricht Deutsch und Englisch – hier wechselst du.", "MonHub speaks English and German – switch here."), "2.6"),
        new("options", "BtnTour", Txt.L("Tour wiederholen", "Repeat the tour"),
            Txt.L("Diese Tour findest du jederzeit hier.", "You'll find this tour here any time."), "2.3"),
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
