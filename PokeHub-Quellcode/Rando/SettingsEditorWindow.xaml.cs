using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace RandoApp;

/// <summary>Quick editor for a randomizer settings file: the most important options in German, the rest under "Experte".</summary>
public partial class SettingsEditorWindow : Window
{
    record Field(string Name, string Label);

    // The options most people care about, grouped like the randomizer's own tabs.
    static readonly (string Group, Field[] Fields)[] Curated =
    [
        ("Starter", [
            new("StartersMod", "Starter"),
            new("RandomizeStartersHeldItems", "Starter tragen zufällige Items"),
        ]),
        ("Statische Pokémon (Legendäre, Geschenke, Fossile …)", [
            new("StaticPokemonMod", "Statische Pokémon"),
            new("StaticLevelModified", "Level ändern"),
            new("StaticLevelModifier", "Level-Änderung in %"),
        ]),
        ("Wilde Pokémon", [
            new("WildPokemonMod", "Wilde Pokémon"),
            new("WildPokemonRestrictionMod", "Einschränkung"),
            new("BlockWildLegendaries", "Keine wilden Legendären"),
            new("UseTimeBasedEncounters", "Tageszeit-Begegnungen mit randomisieren"),
            new("RandomizeWildPokemonHeldItems", "Tragen zufällige Items"),
            new("WildLevelsModified", "Level ändern"),
            new("WildLevelModifier", "Level-Änderung in %"),
            new("UseMinimumCatchRate", "Mindest-Fangrate erhöhen"),
            new("MinimumCatchRateLevel", "Fangrate-Stufe (1–5)"),
        ]),
        ("Trainer", [
            new("TrainersMod", "Trainer-Pokémon"),
            new("TrainersUsePokemonOfSimilarStrength", "Ähnlich starke Pokémon"),
            new("RivalCarriesStarterThroughout", "Rivale behält seinen Starter"),
            new("TrainersBlockLegendaries", "Keine Legendären bei Trainern"),
            new("TrainersForceFullyEvolved", "Ab einem Level voll entwickelt"),
            new("TrainersForceFullyEvolvedLevel", "… ab Level"),
            new("TrainersLevelModified", "Level ändern"),
            new("TrainersLevelModifier", "Level-Änderung in %"),
            new("BetterTrainerMovesets", "Bessere Attacken"),
            new("RandomizeTrainerNames", "Namen randomisieren"),
            new("RandomizeTrainerClassNames", "Klassen-Namen randomisieren"),
        ]),
        ("Pokémon-Werte & Entwicklungen", [
            new("BaseStatisticsMod", "Basiswerte"),
            new("TypesMod", "Typen"),
            new("AbilitiesMod", "Fähigkeiten"),
            new("EvolutionsMod", "Entwicklungen"),
            new("ChangeImpossibleEvolutions", "Unmögliche Entwicklungen ändern (z. B. Tausch → Level)"),
            new("MakeEvolutionsEasier", "Entwicklungen erleichtern"),
            new("StandardizeEXPCurves", "EP-Kurven vereinheitlichen"),
            new("SelectedEXPCurve", "EP-Kurve"),
        ]),
        ("Attacken", [
            new("MovesetsMod", "Level-Attacken"),
            new("TmsMod", "TM-Attacken"),
            new("TmsHmsCompatibilityMod", "TM/VM-Kompatibilität"),
            new("FullHMCompat", "Alle können alle VMs lernen"),
            new("MoveTutorMovesMod", "Attacken-Lehrer"),
            new("MoveTutorsCompatibilityMod", "Lehrer-Kompatibilität"),
        ]),
        ("Items & Tausch", [
            new("FieldItemsMod", "Items auf dem Boden"),
            new("ShopItemsMod", "Shop-Items"),
            new("PickupItemsMod", "Mitnahme-Items"),
            new("InGameTradesMod", "Tausch im Spiel"),
        ]),
    ];

    static readonly Dictionary<string, string> ChoiceLabels = new()
    {
        ["UNCHANGED"] = "Unverändert",
        ["RANDOM"] = "Zufällig",
        ["RANDOMIZE"] = "Zufällig",
        ["COMPLETELY_RANDOM"] = "Komplett zufällig",
        ["SHUFFLE"] = "Gemischt",
        ["CUSTOM"] = "Eigene Auswahl (aus der Datei)",
        ["RANDOM_WITH_TWO_EVOLUTIONS"] = "Zufällig (mit 2 Entwicklungen)",
        ["RANDOM_MATCHING"] = "Zufällig (Legendär ↔ Legendär)",
        ["SIMILAR_STRENGTH"] = "Ähnliche Stärke",
        ["SAME_STRENGTH"] = "Gleiche Stärke",
        ["AREA_MAPPING"] = "1:1 pro Gebiet",
        ["GLOBAL_MAPPING"] = "1:1 im ganzen Spiel",
        ["NONE"] = "Keine",
        ["CATCH_EM_ALL"] = "Alle Pokémon fangbar",
        ["TYPE_THEME_AREAS"] = "Typ-Thema pro Gebiet",
        ["DISTRIBUTED"] = "Gleichmäßig verteilt",
        ["MAINPLAYTHROUGH"] = "Gleichmäßig (nur Hauptspiel)",
        ["TYPE_THEMED"] = "Typ-Themen",
        ["TYPE_THEMED_ELITE4_GYMS"] = "Typ-Themen (nur Arenen & Top 4)",
        ["RANDOM_FOLLOW_EVOLUTIONS"] = "Zufällig (folgt Entwicklungen)",
        ["RANDOM_EVERY_LEVEL"] = "Zufällig (jedes Level)",
        ["RANDOM_PREFER_SAME_TYPE"] = "Zufällig (gleicher Typ bevorzugt)",
        ["RANDOM_PREFER_TYPE"] = "Zufällig (Typ bevorzugt)",
        ["METRONOME_ONLY"] = "Nur Metronom",
        ["FULL"] = "Alle kompatibel",
        ["RANDOM_EVEN"] = "Zufällig (gleichmäßig)",
        ["RANDOMIZE_GIVEN"] = "Nur angebotenes Pokémon",
        ["RANDOMIZE_GIVEN_AND_REQUESTED"] = "Angebotenes & gewünschtes",
        ["SLOW"] = "Langsam",
        ["MEDIUM_SLOW"] = "Mittel-langsam",
        ["MEDIUM_FAST"] = "Mittel-schnell",
        ["FAST"] = "Schnell",
        ["ERRATIC"] = "Unregelmäßig",
        ["FLUCTUATING"] = "Schwankend",
        ["LEGENDARIES"] = "Legendäre",
        ["STRONG_LEGENDARIES"] = "Starke Legendäre",
        ["ALL"] = "Alle",
    };

    // Not editable here: bit field / read-only / meta values.
    static readonly HashSet<string> Hidden = ["RomName", "CurrentMiscTweaks", "UpdatedFromOldVersion"];

    record Choice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    readonly AppConfig _cfg;
    readonly string _path;
    readonly Dictionary<string, RandoOption> _options;
    readonly Dictionary<string, string> _changes = new();

    /// <summary>Path of the saved file (the original or the new one) after DialogResult == true.</summary>
    public string? SavedPath { get; private set; }

    public SettingsEditorWindow(AppConfig cfg, string path, List<RandoOption> options)
    {
        InitializeComponent();
        WindowFit.Apply(this);
        _cfg = cfg;
        _path = path;
        _options = options.ToDictionary(o => o.Name);

        TxtFile.Text = Path.GetFileName(path);
        var romName = _options.TryGetValue("RomName", out var rn) ? rn.Value : "?";
        TxtMadeWith.Text = $"Erstellt mit: {romName}.  Nicht jede Option gibt es in jedem Spiel – was ein Spiel nicht kann, lässt der Randomizer weg.";

        var curatedNames = new HashSet<string>();
        foreach (var (group, fields) in Curated)
        {
            var present = fields.Where(f => _options.ContainsKey(f.Name)).ToList();
            if (present.Count == 0) continue;
            Groups.Children.Add(BuildGroup(group, present.Select(f => (_options[f.Name], f.Label))));
            curatedNames.UnionWith(present.Select(f => f.Name));
        }

        var rest = options.Where(o => !curatedNames.Contains(o.Name) && !Hidden.Contains(o.Name)).ToList();
        var expert = new Expander { Header = $"Alle weiteren Optionen (Experte, {rest.Count})", Margin = new Thickness(0, 4, 0, 0) };
        expert.Content = BuildGroup(null, rest.Select(o => (o, SplitCamelCase(o.Name))));
        Groups.Children.Add(expert);

        UpdateChangeCount();
    }

    static string SplitCamelCase(string s) => Regex.Replace(s, "(?<=[a-z0-9])(?=[A-Z])", " ");

    FrameworkElement BuildGroup(string? title, IEnumerable<(RandoOption Option, string Label)> rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

        var panel = new StackPanel();
        if (title != null)
            panel.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Hub.Heading"), Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(grid);

        foreach (var (option, label) in rows)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var control = BuildControl(option, label);
            if (option.Type == "bool")
            {
                // Checkboxes carry their own label and span both columns.
                Grid.SetColumnSpan(control, 2);
            }
            else
            {
                var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 12, 4) };
                Grid.SetRow(lbl, row);
                grid.Children.Add(lbl);
                Grid.SetColumn(control, 1);
            }
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }
        return new ContentControl { Style = (Style)FindResource("Hub.Panel"), Margin = new Thickness(0, 0, 0, 14), Content = panel };
    }

    FrameworkElement BuildControl(RandoOption option, string label)
    {
        switch (option.Type)
        {
            case "bool":
                var chk = new CheckBox { Content = label, IsChecked = option.Value == "true", Margin = new Thickness(0, 2, 0, 2) };
                chk.Checked += (_, _) => SetValue(option, "true");
                chk.Unchecked += (_, _) => SetValue(option, "false");
                return chk;

            case "enum":
                // "Custom" starters need a starter list that only the original GUI can set, so only keep it if already chosen.
                var choices = option.Choices
                    .Where(c => c != "CUSTOM" || option.Value == "CUSTOM")
                    .Select(c => new Choice(c, ChoiceLabels.GetValueOrDefault(c, c)))
                    .ToList();
                var cmb = new ComboBox { ItemsSource = choices, Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Stretch };
                cmb.SelectedItem = choices.FirstOrDefault(c => c.Value == option.Value);
                cmb.SelectionChanged += (_, _) =>
                {
                    if (cmb.SelectedItem is Choice c) SetValue(option, c.Value);
                };
                return cmb;

            default: // int
                var txt = new TextBox { Text = option.Value, Width = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3) };
                var normalBorder = txt.BorderBrush;
                txt.TextChanged += (_, _) =>
                {
                    bool valid = int.TryParse(txt.Text.Trim(), out var n);
                    txt.BorderBrush = valid ? normalBorder : (Brush)FindResource("Hub.Danger");
                    txt.Tag = valid ? null : "invalid";
                    if (valid) SetValue(option, n.ToString());
                };
                return txt;
        }
    }

    void SetValue(RandoOption option, string value)
    {
        if (value == option.Value) _changes.Remove(option.Name);
        else _changes[option.Name] = value;
        UpdateChangeCount();
    }

    void UpdateChangeCount()
    {
        TxtChanges.Text = _changes.Count switch
        {
            0 => "Keine Änderungen",
            1 => "1 Änderung",
            var n => $"{n} Änderungen",
        };
        BtnOverwrite.IsEnabled = _changes.Count > 0;
    }

    bool HasInvalidInput() =>
        FindInvalid(Groups) is { } bad && MessageBox.Show(this, "Mindestens ein Zahlenfeld enthält keine gültige Zahl (rot umrandet).",
            "Settings bearbeiten", MessageBoxButton.OK, MessageBoxImage.Warning) == MessageBoxResult.OK;

    static DependencyObject? FindInvalid(DependencyObject root)
    {
        if (root is TextBox { Tag: "invalid" }) return root;
        IEnumerable<object> children = root is Expander { Content: DependencyObject c } ? [c] : LogicalTreeHelper.GetChildren(root).Cast<object>();
        foreach (var child in children.OfType<DependencyObject>())
            if (FindInvalid(child) is { } hit) return hit;
        return null;
    }

    async Task SaveTo(string target)
    {
        IsEnabled = false;
        try
        {
            await RandoSettingsFile.SaveAsync(_cfg, _path, target, _changes);
            SavedPath = target;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Speichern fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    async void BtnOverwrite_Click(object sender, RoutedEventArgs e)
    {
        if (HasInvalidInput()) return;
        var count = _changes.Count == 1 ? "1 Änderung" : $"{_changes.Count} Änderungen";
        var answer = MessageBox.Show(this, $"„{Path.GetFileName(_path)}“ mit {count} überschreiben?",
            "Überschreiben", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            await SaveTo(_path);
    }

    async void BtnSaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (HasInvalidInput()) return;
        var dlg = new SaveFileDialog
        {
            Filter = "Randomizer Settings (*.rnqs)|*.rnqs",
            InitialDirectory = Path.GetDirectoryName(_path),
            FileName = Path.GetFileNameWithoutExtension(_path) + "_neu.rnqs",
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) == true)
            await SaveTo(dlg.FileName);
    }
}
