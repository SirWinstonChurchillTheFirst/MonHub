using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace MonHub;

/// <summary>A theme in the gallery, with a tiny MonHub drawn in its colours.</summary>
public class ThemeChoice(HubTheme theme)
{
    public HubTheme Theme { get; } = theme;
    public Brush Background { get; } = Solid(theme.PreviewBackground);
    public Brush Rail { get; } = Solid(theme.PreviewRail);
    public Brush Surface { get; } = Solid(theme.PreviewSurface);
    public Brush Accent { get; } = Solid(theme.PreviewAccent);
    public Brush Ink { get; } = Solid(theme.PreviewInk);
    /// <summary>Lines in the menu: light on a dark menu, dark on a light one.</summary>
    public Brush RailInk { get; } = Solid(Luminance(theme.PreviewRail) < 0.5 ? "#F2F2F2" : theme.PreviewInk);
    public int RailColumn => Theme.RailRight ? 2 : 0;
    public GridLength LeftRail => Theme.RailRight ? new GridLength(0) : new GridLength(50);
    public GridLength RightRail => Theme.RailRight ? new GridLength(50) : new GridLength(0);
    public bool IsCurrent => HubThemes.Current.Id == Theme.Id;
    public bool CurrentVisibility => IsCurrent;

    static SolidColorBrush Solid(string hex)
    {
        return new SolidColorBrush(Color.Parse(hex));
    }

    static double Luminance(string hex)
    {
        var c = Color.Parse(hex);
        return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
    }
}

/// <summary>"Optionen": theme, motion, trainer name, the MonHub folder, help and credits.</summary>
public partial class OptionsPage : UserControl, IHubPage
{
    bool _updating;

    public OptionsPage()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TxtCredits.Text = Txt.L(
            $"MonHub {version?.ToString(3)} · freie Software (GPL-3.0) · Sprites und Porträts: PMD Sprite Collab (sprites.pmdcollab.org, CC BY-NC 4.0 – " +
            "alle Künstler in CREDITS.md) · Schriften: Silkscreen, Pixelify Sans, Press Start 2P (SIL Open Font License) · " +
            "Randomizer: Universal Pokemon Randomizer ZX (GPL-3.0). Ein inoffizielles, nicht-kommerzielles Fanprojekt ohne Verbindung zu " +
            "Nintendo, Creatures, GAME FREAK oder The Pokémon Company; Pokémon und alle Namen gehören ihren Inhabern.",
            $"MonHub {version?.ToString(3)} · free software (GPL-3.0) · Sprites and portraits: PMD Sprite Collab (sprites.pmdcollab.org, CC BY-NC 4.0 – " +
            "all artists in CREDITS.md) · Fonts: Silkscreen, Pixelify Sans, Press Start 2P (SIL Open Font License) · " +
            "Randomizer: Universal Pokemon Randomizer ZX (GPL-3.0). An unofficial, non-commercial fan project not affiliated with " +
            "Nintendo, Creatures, GAME FREAK or The Pokémon Company; Pokémon and all names belong to their owners.");
    }

    public void Refresh()
    {
        _updating = true;
        ThemeList.ItemsSource = HubThemes.All.Select(t => new ThemeChoice(t)).ToList();
        var config = HubConfig.Current;
        (config.Motion switch
        {
            null => MotionAuto,
            MotionLevel.All => MotionAll,
            MotionLevel.Calm => MotionCalm,
            _ => MotionOff,
        }).IsChecked = true;
        (Txt.English ? LangEnglish : LangGerman).IsChecked = true;
        if (!TxtTrainer.IsFocused) TxtTrainer.Text = config.TrainerName;
        TxtRoot.Text = HubPaths.Root;
        TxtRoot.ToolTip = HubPaths.Root;
        _updating = false;
        UpdateMotionText();
        UpdateTrainerHint();
    }

    void Theme_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ThemeChoice choice || choice.IsCurrent) return;
        HubThemes.Apply(choice.Theme.Id);
        HubConfig.Current.Theme = choice.Theme.Id;
        HubConfig.Current.Save();
        Refresh();
    }

    void Motion_Checked(object? sender, RoutedEventArgs e)
    {
        if (_updating) return;
        MotionLevel? level = sender == MotionAll ? MotionLevel.All : sender == MotionCalm ? MotionLevel.Calm : sender == MotionOff ? MotionLevel.Off : null;
        HubConfig.Current.Motion = level;
        HubConfig.Current.Save();
        HubMotion.Apply(HubConfig.Current.EffectiveMotion);
        UpdateMotionText();
    }

    void UpdateMotionText()
    {
        var config = HubConfig.Current;
        TxtMotion.Text = config.Motion switch
        {
            null when !Os.Windows => Txt.L("Alles bewegt sich. (Linux-Desktops haben keinen gemeinsamen Schalter für weniger Bewegung – stell es hier ein.)",
                                         "Everything moves. (Linux desktops have no common switch for less motion – set it here.)"),
            null => Compat.SystemAnimations
                ? Txt.L("Folgt der Windows-Einstellung „Animationseffekte“ – die ist gerade an, also bewegt sich alles.",
                        "Follows the Windows setting “Animation effects” – it is on right now, so everything moves.")
                : Txt.L("Folgt der Windows-Einstellung „Animationseffekte“ – die ist gerade aus, also bewegt sich nichts.",
                        "Follows the Windows setting “Animation effects” – it is off right now, so nothing moves."),
            MotionLevel.All => Txt.L("Pokémon bewegen sich, und die Details des Themes auch (LEDs, Cursor, Ringe, Gras). Alles pausiert, sobald MonHub im Hintergrund ist.",
                "Pokémon move, and so do the theme's details (LEDs, cursor, rings, grass). Everything pauses as soon as MonHub is in the background."),
            MotionLevel.Calm => Txt.L("Nur das große Pokémon des Spiels bewegt sich. Keine Theme-Animationen, das Team steht still.",
                "Only the game's big Pokémon moves. No theme animations, the team stands still."),
            _ => Txt.L("Nichts bewegt sich – auch keine Seitenübergänge.", "Nothing moves – not even page transitions."),
        };
    }

    /// <summary>The language is fixed for one start (pages and tables are built once) – so MonHub restarts in the new one.</summary>
    void Language_Checked(object? sender, RoutedEventArgs e)
    {
        if (_updating) return;
        var code = sender == LangGerman ? "de" : "en";
        if (code == Txt.Code) return;
        HubConfig.Current.Language = code;
        HubConfig.Current.Save();
        App.Restart();
    }

    void Trainer_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateTrainerHint();
        if (_updating) return;
        HubConfig.Current.TrainerName = (TxtTrainer.Text ?? "").Trim();
        HubConfig.Current.Save();
    }

    void UpdateTrainerHint()
    {
        TrainerHint.Text = (TxtTrainer.Text ?? "").Length == 0 ? Txt.L("Name aus dem Spielstand", "Name from the save") : "";
    }

    void OpenRoot_Click(object? sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.Root);

    void Tour_Click(object? sender, RoutedEventArgs e) => Shell.StartTour();

    void Import_Click(object? sender, RoutedEventArgs e)
    {
        new ImportWindow { Owner = Compat.WindowOf(this) }.ShowDialog();
        Refresh();
    }

    void Help_Click(object? sender, RoutedEventArgs e) => new HelpWindow { Owner = Compat.WindowOf(this) }.ShowDialog();

    void Readme_Click(object? sender, RoutedEventArgs e)
    {
        // the guide in the chosen language, the other one if only that is there
        var readme = new[] { Txt.L("LIESMICH.txt", "README.txt"), Txt.L("README.txt", "LIESMICH.txt") }
            .Select(name => Path.Combine(HubPaths.Root, name)).FirstOrDefault(File.Exists);
        if (readme != null) Process.Start(new ProcessStartInfo(readme) { UseShellExecute = true });
        else MessageBox.Show(Compat.WindowOf(this), Txt.L("Die LIESMICH-Datei fehlt – sie kommt mit dem Setup wieder.", "The README file is missing – the setup brings it back."),
            "MonHub", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
