using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PokeHub;

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
    public string? Tag => IsCurrent ? "Selected" : null;
    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    static SolidColorBrush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    static double Luminance(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
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
        TxtCredits.Text =
            $"MonHub {version?.ToString(3)} · freie Software (GPL-3.0) · Sprites und Porträts: PMD Sprite Collab (sprites.pmdcollab.org, CC BY-NC 4.0 – " +
            "alle Künstler in CREDITS.md) · Schriften: Silkscreen, Pixelify Sans, Press Start 2P (SIL Open Font License) · " +
            "Randomizer: Universal Pokemon Randomizer ZX (GPL-3.0). Ein inoffizielles, nicht-kommerzielles Fanprojekt ohne Verbindung zu " +
            "Nintendo, Creatures, GAME FREAK oder The Pokémon Company; Pokémon und alle Namen gehören ihren Inhabern.";
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
        if (!TxtTrainer.IsKeyboardFocused) TxtTrainer.Text = config.TrainerName;
        TxtRoot.Text = HubPaths.Root;
        TxtRoot.ToolTip = HubPaths.Root;
        _updating = false;
        UpdateMotionText();
        UpdateTrainerHint();
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ThemeChoice choice || choice.IsCurrent) return;
        HubThemes.Apply(choice.Theme.Id);
        HubConfig.Current.Theme = choice.Theme.Id;
        HubConfig.Current.Save();
        Refresh();
    }

    void Motion_Checked(object sender, RoutedEventArgs e)
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
            null => $"Folgt der Windows-Einstellung „Animationseffekte“ – die ist gerade {(SystemParameters.ClientAreaAnimation ? "an, also bewegt sich alles" : "aus, also bewegt sich nichts")}.",
            MotionLevel.All => "Pokémon bewegen sich, und die Details des Themes auch (LEDs, Cursor, Ringe, Gras). Alles pausiert, sobald MonHub im Hintergrund ist.",
            MotionLevel.Calm => "Nur das große Pokémon des Spiels bewegt sich. Keine Theme-Animationen, das Team steht still.",
            _ => "Nichts bewegt sich – auch keine Seitenübergänge.",
        };
    }

    void Trainer_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTrainerHint();
        if (_updating) return;
        HubConfig.Current.TrainerName = TxtTrainer.Text.Trim();
        HubConfig.Current.Save();
    }

    void UpdateTrainerHint()
    {
        TrainerHint.Text = TxtTrainer.Text.Length == 0 ? "Name aus dem Spielstand" : "";
    }

    void OpenRoot_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.Root);

    void Tour_Click(object sender, RoutedEventArgs e) => Shell.StartTour();

    void Import_Click(object sender, RoutedEventArgs e)
    {
        new ImportWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        Refresh();
    }

    void Help_Click(object sender, RoutedEventArgs e) => new HelpWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    void Readme_Click(object sender, RoutedEventArgs e)
    {
        var readme = Path.Combine(HubPaths.Root, "LIESMICH.txt");
        if (File.Exists(readme)) Process.Start(new ProcessStartInfo(readme) { UseShellExecute = true });
        else MessageBox.Show(Window.GetWindow(this), "Die LIESMICH-Datei fehlt – sie kommt mit dem Setup wieder.", "MonHub", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
