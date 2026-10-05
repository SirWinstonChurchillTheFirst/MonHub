using System.IO;
using System.Text.RegularExpressions;

using Txt = MonHub.Txt;

namespace RandoApp;

/// <summary>Quick editor for a randomizer settings file: the most important options with plain names, the rest under "Experte".</summary>
public partial class SettingsEditorWindow : HubWindow
{
    record Field(string Name, string Label);

    // The options most people care about, grouped like the randomizer's own tabs.
    static readonly (string Group, Field[] Fields)[] Curated =
    [
        (Txt.L("Starter", "Starters"), [
            new("StartersMod", Txt.L("Starter", "Starters")),
            new("RandomizeStartersHeldItems", Txt.L("Starter tragen zufällige Items", "Starters hold random items")),
        ]),
        (Txt.L("Statische Pokémon (Legendäre, Geschenke, Fossile …)", "Static Pokémon (legendaries, gifts, fossils …)"), [
            new("StaticPokemonMod", Txt.L("Statische Pokémon", "Static Pokémon")),
            new("StaticLevelModified", Txt.L("Level ändern", "Change levels")),
            new("StaticLevelModifier", Txt.L("Level-Änderung in %", "Level change in %")),
        ]),
        (Txt.L("Wilde Pokémon", "Wild Pokémon"), [
            new("WildPokemonMod", Txt.L("Wilde Pokémon", "Wild Pokémon")),
            new("WildPokemonRestrictionMod", Txt.L("Einschränkung", "Restriction")),
            new("BlockWildLegendaries", Txt.L("Keine wilden Legendären", "No wild legendaries")),
            new("UseTimeBasedEncounters", Txt.L("Tageszeit-Begegnungen mit randomisieren", "Randomize time-of-day encounters too")),
            new("RandomizeWildPokemonHeldItems", Txt.L("Tragen zufällige Items", "Hold random items")),
            new("WildLevelsModified", Txt.L("Level ändern", "Change levels")),
            new("WildLevelModifier", Txt.L("Level-Änderung in %", "Level change in %")),
            new("UseMinimumCatchRate", Txt.L("Mindest-Fangrate erhöhen", "Raise the minimum catch rate")),
            new("MinimumCatchRateLevel", Txt.L("Fangrate-Stufe (1–5)", "Catch rate level (1–5)")),
        ]),
        (Txt.L("Trainer", "Trainers"), [
            new("TrainersMod", Txt.L("Trainer-Pokémon", "Trainer Pokémon")),
            new("TrainersUsePokemonOfSimilarStrength", Txt.L("Ähnlich starke Pokémon", "Pokémon of similar strength")),
            new("RivalCarriesStarterThroughout", Txt.L("Rivale behält seinen Starter", "Rival keeps their starter")),
            new("TrainersBlockLegendaries", Txt.L("Keine Legendären bei Trainern", "No legendaries for trainers")),
            new("TrainersForceFullyEvolved", Txt.L("Ab einem Level voll entwickelt", "Fully evolved from a level on")),
            new("TrainersForceFullyEvolvedLevel", Txt.L("… ab Level", "… from level")),
            new("TrainersLevelModified", Txt.L("Level ändern", "Change levels")),
            new("TrainersLevelModifier", Txt.L("Level-Änderung in %", "Level change in %")),
            new("BetterTrainerMovesets", Txt.L("Bessere Attacken", "Better movesets")),
            new("RandomizeTrainerNames", Txt.L("Namen randomisieren", "Randomize names")),
            new("RandomizeTrainerClassNames", Txt.L("Klassen-Namen randomisieren", "Randomize class names")),
        ]),
        (Txt.L("Pokémon-Werte & Entwicklungen", "Pokémon stats & evolutions"), [
            new("BaseStatisticsMod", Txt.L("Basiswerte", "Base stats")),
            new("TypesMod", Txt.L("Typen", "Types")),
            new("AbilitiesMod", Txt.L("Fähigkeiten", "Abilities")),
            new("EvolutionsMod", Txt.L("Entwicklungen", "Evolutions")),
            new("ChangeImpossibleEvolutions", Txt.L("Unmögliche Entwicklungen ändern (z. B. Tausch → Level)", "Change impossible evolutions (e.g. trade → level)")),
            new("MakeEvolutionsEasier", Txt.L("Entwicklungen erleichtern", "Make evolutions easier")),
            new("StandardizeEXPCurves", Txt.L("EP-Kurven vereinheitlichen", "Standardize EXP curves")),
            new("SelectedEXPCurve", Txt.L("EP-Kurve", "EXP curve")),
        ]),
        (Txt.L("Attacken", "Moves"), [
            new("MovesetsMod", Txt.L("Level-Attacken", "Level-up moves")),
            new("TmsMod", Txt.L("TM-Attacken", "TM moves")),
            new("TmsHmsCompatibilityMod", Txt.L("TM/VM-Kompatibilität", "TM/HM compatibility")),
            new("FullHMCompat", Txt.L("Alle können alle VMs lernen", "Everyone can learn every HM")),
            new("MoveTutorMovesMod", Txt.L("Attacken-Lehrer", "Move tutors")),
            new("MoveTutorsCompatibilityMod", Txt.L("Lehrer-Kompatibilität", "Tutor compatibility")),
        ]),
        (Txt.L("Items & Tausch", "Items & trades"), [
            new("FieldItemsMod", Txt.L("Items auf dem Boden", "Field items")),
            new("ShopItemsMod", Txt.L("Shop-Items", "Shop items")),
            new("PickupItemsMod", Txt.L("Mitnahme-Items", "Pickup items")),
            new("InGameTradesMod", Txt.L("Tausch im Spiel", "In-game trades")),
        ]),
    ];

    static readonly Dictionary<string, string> ChoiceLabels = new()
    {
        ["UNCHANGED"] = Txt.L("Unverändert", "Unchanged"),
        ["RANDOM"] = Txt.L("Zufällig", "Random"),
        ["RANDOMIZE"] = Txt.L("Zufällig", "Random"),
        ["COMPLETELY_RANDOM"] = Txt.L("Komplett zufällig", "Completely random"),
        ["SHUFFLE"] = Txt.L("Gemischt", "Shuffled"),
        ["CUSTOM"] = Txt.L("Eigene Auswahl (aus der Datei)", "Custom (from the file)"),
        ["RANDOM_WITH_TWO_EVOLUTIONS"] = Txt.L("Zufällig (mit 2 Entwicklungen)", "Random (with 2 evolutions)"),
        ["RANDOM_MATCHING"] = Txt.L("Zufällig (Legendär ↔ Legendär)", "Random (legendary ↔ legendary)"),
        ["SIMILAR_STRENGTH"] = Txt.L("Ähnliche Stärke", "Similar strength"),
        ["SAME_STRENGTH"] = Txt.L("Gleiche Stärke", "Same strength"),
        ["AREA_MAPPING"] = Txt.L("1:1 pro Gebiet", "1:1 per area"),
        ["GLOBAL_MAPPING"] = Txt.L("1:1 im ganzen Spiel", "1:1 across the game"),
        ["NONE"] = Txt.L("Keine", "None"),
        ["CATCH_EM_ALL"] = Txt.L("Alle Pokémon fangbar", "Catch 'em all"),
        ["TYPE_THEME_AREAS"] = Txt.L("Typ-Thema pro Gebiet", "Type theme per area"),
        ["DISTRIBUTED"] = Txt.L("Gleichmäßig verteilt", "Evenly distributed"),
        ["MAINPLAYTHROUGH"] = Txt.L("Gleichmäßig (nur Hauptspiel)", "Even (main game only)"),
        ["TYPE_THEMED"] = Txt.L("Typ-Themen", "Type themed"),
        ["TYPE_THEMED_ELITE4_GYMS"] = Txt.L("Typ-Themen (nur Arenen & Top 4)", "Type themed (gyms & Elite Four only)"),
        ["RANDOM_FOLLOW_EVOLUTIONS"] = Txt.L("Zufällig (folgt Entwicklungen)", "Random (follows evolutions)"),
        ["RANDOM_EVERY_LEVEL"] = Txt.L("Zufällig (jedes Level)", "Random (every level)"),
        ["RANDOM_PREFER_SAME_TYPE"] = Txt.L("Zufällig (gleicher Typ bevorzugt)", "Random (same type preferred)"),
        ["RANDOM_PREFER_TYPE"] = Txt.L("Zufällig (Typ bevorzugt)", "Random (type preferred)"),
        ["METRONOME_ONLY"] = Txt.L("Nur Metronom", "Metronome only"),
        ["FULL"] = Txt.L("Alle kompatibel", "All compatible"),
        ["RANDOM_EVEN"] = Txt.L("Zufällig (gleichmäßig)", "Random (even)"),
        ["RANDOMIZE_GIVEN"] = Txt.L("Nur angebotenes Pokémon", "Offered Pokémon only"),
        ["RANDOMIZE_GIVEN_AND_REQUESTED"] = Txt.L("Angebotenes & gewünschtes", "Offered & requested"),
        ["SLOW"] = Txt.L("Langsam", "Slow"),
        ["MEDIUM_SLOW"] = Txt.L("Mittel-langsam", "Medium slow"),
        ["MEDIUM_FAST"] = Txt.L("Mittel-schnell", "Medium fast"),
        ["FAST"] = Txt.L("Schnell", "Fast"),
        ["ERRATIC"] = Txt.L("Unregelmäßig", "Erratic"),
        ["FLUCTUATING"] = Txt.L("Schwankend", "Fluctuating"),
        ["LEGENDARIES"] = Txt.L("Legendäre", "Legendaries"),
        ["STRONG_LEGENDARIES"] = Txt.L("Starke Legendäre", "Strong legendaries"),
        ["ALL"] = Txt.L("Alle", "All"),
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
        TxtMadeWith.Text = Txt.L($"Erstellt mit: {romName}.  Nicht jede Option gibt es in jedem Spiel – was ein Spiel nicht kann, lässt der Randomizer weg.",
            $"Made with: {romName}.  Not every option exists in every game – what a game can't do, the randomizer leaves out.");

        var curatedNames = new HashSet<string>();
        foreach (var (group, fields) in Curated)
        {
            var present = fields.Where(f => _options.ContainsKey(f.Name)).ToList();
            if (present.Count == 0) continue;
            Groups.Children.Add(BuildGroup(group, present.Select(f => (_options[f.Name], f.Label))));
            curatedNames.UnionWith(present.Select(f => f.Name));
        }

        var rest = options.Where(o => !curatedNames.Contains(o.Name) && !Hidden.Contains(o.Name)).ToList();
        var expert = new Expander { Header = Txt.L($"Alle weiteren Optionen (Experte, {rest.Count})", $"All other options (expert, {rest.Count})"), Margin = new Thickness(0, 4, 0, 0) };
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
            panel.Children.Add(new TextBlock { Text = title, Classes = { "heading" }, Margin = new Thickness(0, 0, 0, 8) });
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
        return new ContentControl { Theme = (Avalonia.Styling.ControlTheme)this.FindResource("Hub.Panel")!, Margin = new Thickness(0, 0, 0, 14), Content = panel };
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
                    txt.BorderBrush = valid ? normalBorder : (Brush)this.FindResource("Hub.Danger")!;
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
            0 => Txt.L("Keine Änderungen", "No changes"),
            1 => Txt.L("1 Änderung", "1 change"),
            var n => Txt.L($"{n} Änderungen", $"{n} changes"),
        };
        BtnOverwrite.IsEnabled = _changes.Count > 0;
    }

    bool HasInvalidInput() =>
        FindInvalid(Groups) is { } bad && MessageBox.Show(this, Txt.L("Mindestens ein Zahlenfeld enthält keine gültige Zahl (rot umrandet).", "At least one number field doesn't hold a valid number (outlined in red)."),
            Txt.L("Settings bearbeiten", "Edit settings"), MessageBoxButton.OK, MessageBoxImage.Warning) == MessageBoxResult.OK;

    static DependencyObject? FindInvalid(DependencyObject root)
    {
        if (root is TextBox { Tag: "invalid" }) return root;
        IEnumerable<object> children = root is Expander { Content: DependencyObject c } ? [c] : (root as Avalonia.LogicalTree.ILogical)?.LogicalChildren.Cast<object>() ?? [];
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
            MessageBox.Show(this, ex.Message, Txt.L("Speichern fehlgeschlagen", "Saving failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    async void BtnOverwrite_Click(object? sender, RoutedEventArgs e)
    {
        if (HasInvalidInput()) return;
        var file = Path.GetFileName(_path);
        var answer = MessageBox.Show(this, _changes.Count == 1
                ? Txt.L($"„{file}“ mit 1 Änderung überschreiben?", $"Overwrite “{file}” with 1 change?")
                : Txt.L($"„{file}“ mit {_changes.Count} Änderungen überschreiben?", $"Overwrite “{file}” with {_changes.Count} changes?"),
            Txt.L("Überschreiben", "Overwrite"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            await SaveTo(_path);
    }

    async void BtnSaveAs_Click(object? sender, RoutedEventArgs e)
    {
        if (HasInvalidInput()) return;
        var file = Pick.SaveFile(this, Txt.L("Speichern unter", "Save as"), Path.GetFileNameWithoutExtension(_path) + Txt.L("_neu.rnqs", "_new.rnqs"),
            [("Randomizer Settings (*.rnqs)", ["*.rnqs"])], Path.GetDirectoryName(_path), "rnqs");
        if (file != null)
            await SaveTo(file);
    }
}
