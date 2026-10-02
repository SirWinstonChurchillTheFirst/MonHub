using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RandoApp;

namespace PokeHub;

/// <summary>An original ROM in the list: the ROM plus how MonHub shows its game.</summary>
public class RunRom(RomInfo rom)
{
    public RomInfo Rom { get; } = rom;

    /// <summary>3DS header (the shared ROM reader only knows DS and Game Boy).</summary>
    readonly Rom3ds? _n3ds = Rom3ds.IsFile(rom.Path) ? Rom3ds.Read(rom.Path) : null;

    public GameCard Card => _card ??= new(new GameEntry
    {
        Rom = Rom.Path,
        IsRun = false,
        System = _n3ds != null ? GameSystem.N3DS
               : Rom.Platform switch { RomPlatform.Nds => GameSystem.DS, RomPlatform.Gba => GameSystem.GBA, RomPlatform.Gb => GameSystem.GB, _ => GameSystem.Other },
        RomTime = File.GetLastWriteTime(Rom.Path),
        GameCode = _n3ds?.GameCode ?? Rom.GameCode,
        HeaderTitle = Rom.HeaderTitle,
        TitleId = _n3ds?.TitleId ?? 0,
    });
    GameCard? _card;

    public bool Is3DS => _n3ds != null;

    public string Detail => Rom.Folder.Length > 0 ? $"{Card.SystemText} · {Rom.Folder}" : Card.SystemText;

    public string Edition => Card.Look.Version.Length > 0 ? Card.Look.Version : "Andere";

    public string Language => Is3DS ? "Mehrsprachig" : GameLook.Region(Rom.GameCode) switch
    {
        "DE" => "Deutsch",
        "EN" => "Englisch",
        "FR" => "Französisch",
        "IT" => "Italienisch",
        "ES" => "Spanisch",
        "JP" => "Japanisch",
        "KR" => "Koreanisch",
        _ => "Unbekannt",
    };
}

/// <summary>A settings preset (.rnqs) in the list.</summary>
public record Preset(string Path)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path).Replace('_', ' '); // "Randomizer_Hard_Trainers" -> "Randomizer Hard Trainers"
    public override string ToString() => Name;
}

/// <summary>
/// "Neuer Run": der Randomizer's randomizer inside MonHub – same settings file (PokeRando.json), same steps (unique name,
/// PokeRandoZX via RandoHelper, own name, spoiler, Rare Candy cheat file), in MonHub's look.
/// </summary>
public partial class RandomizerPage : UserControl, IHubPage
{
    AppConfig _cfg = new();
    List<RunRom> _roms = [];
    string? _lastOutput;
    RomInfo? _lastRom;
    bool _busy, _loading;

    public RandomizerPage()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    RomInfo? SelectedRom => (RomList.SelectedItem as RunRom)?.Rom;

    int _loadId;
    string? _shownRoms;

    /// <summary>Settings at once; the ROM folder is read in the background and the list only rebuilt when it changed.</summary>
    public async void Refresh()
    {
        if (_busy) return;
        AppConfig.ConfigOverride = HubPaths.PokeRandoConfig;
        _cfg = AppConfig.Load();
        _loading = true;
        ChkAskName.IsChecked = _cfg.AskForName;
        ChkSpoilerFile.IsChecked = _cfg.CreateSpoilerFile;
        ChkFullLog.IsChecked = _cfg.CreateLog;
        LoadPresets();
        _loading = false;

        int id = ++_loadId;
        var folder = _cfg.RomFolder;
        List<RunRom> roms;
        try
        {
            roms = await Task.Run(() => ScanRoms(folder));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return;
        }
        if (id != _loadId || _busy) return;
        var signature = folder + "|" + string.Join("|", roms.Select(r => r.Rom.Path + "*" + r.Card.Game.RomTime.Ticks));
        _loading = true;
        if (signature != _shownRoms)
        {
            _shownRoms = signature;
            ShowScanned(roms);
        }
        _loading = false;
        UpdateGame();
    }

    /// <summary>The ROM folder like der Randomizer reads it: sub folders too, folders starting with "_" skipped. Runs off the UI thread.</summary>
    static List<RunRom> ScanRoms(string folder)
    {
        // everything the randomizer can mix – the hub has its own visible filters (der Randomizer's file type setting doesn't apply here)
        var shownTypes = new HashSet<string>([".nds", ".gba", ".gb", ".gbc", ".3ds", ".cci", ".cxi"], StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return [];
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            var roms = Directory.EnumerateFiles(folder, "*", options)
                .Where(f => shownTypes.Contains(Path.GetExtension(f)))
                .Select(f => (Path: f, Folder: Path.GetDirectoryName(Path.GetRelativePath(folder, f)) ?? ""))
                .Where(x => !x.Folder.Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('_')))
                .OrderBy(x => x.Folder.Length == 0 ? 0 : 1)
                .ThenBy(x => x.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => Path.GetFileName(x.Path), StringComparer.OrdinalIgnoreCase)
                .Select(x => new RunRom(new RomInfo(x.Path) { Folder = x.Folder.Replace(Path.DirectorySeparatorChar.ToString(), " › ") }))
                .ToList();
            foreach (var rom in roms) _ = rom.Card.Game.RomTime; // header and game card read here, not on the UI thread
            return roms;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return []; // ROM folder on a drive that just went away – empty list instead of a crash
        }
    }

    void ShowScanned(List<RunRom> roms)
    {
        var previous = SelectedRom?.Path ?? _cfg.LastRom;
        _roms = roms;
        FillFilter(CmbSystem, "Alle Systeme", roms.Select(r => r.Card.SystemText));
        FillFilter(CmbEdition, "Alle Editionen", roms.Select(r => r.Edition));
        FillFilter(CmbLanguage, "Alle Sprachen", roms.Select(r => r.Language));
        TxtCount.Text = roms.Count switch
        {
            0 => "Noch keine Originale im ROM-Ordner.",
            1 => "1 Original im ROM-Ordner – daraus wird ein neuer Run.",
            _ => $"{roms.Count} Originale im ROM-Ordner – wähl eins, daraus wird ein neuer Run.",
        };
        ShowRoms(previous);
    }

    /// <summary>"Alle …" plus the values that occur; the choice stays when it still exists.</summary>
    static void FillFilter(ComboBox box, string all, IEnumerable<string> values)
    {
        var previous = box.SelectedItem as string;
        var items = values.Distinct().OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).Prepend(all).ToList();
        box.ItemsSource = items;
        box.SelectedItem = previous != null && items.Contains(previous) ? previous : all;
    }

    void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) Search_TextChanged(sender, null!);
    }

    /// <summary>The list for edition, language and search words; keeps the chosen ROM when it still matches.</summary>
    void ShowRoms(string? keep)
    {
        var words = TxtSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var system = CmbSystem.SelectedIndex > 0 ? CmbSystem.SelectedItem as string : null;
        var edition = CmbEdition.SelectedIndex > 0 ? CmbEdition.SelectedItem as string : null;
        var language = CmbLanguage.SelectedIndex > 0 ? CmbLanguage.SelectedItem as string : null;
        var visible = _roms.Where(r => (system == null || r.Card.SystemText == system) && (edition == null || r.Edition == edition) && (language == null || r.Language == language) && words.All(w => r.Card.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                                  || r.Card.Look.Version.Contains(w, StringComparison.OrdinalIgnoreCase)
                                                  || r.Rom.Folder.Contains(w, StringComparison.OrdinalIgnoreCase)))
            // "Rote" finds "Feuerrote" too – games where the words start a word ("Rote Edition") come first
            .OrderByDescending(r => words.Count(w => StartsAWord(r.Card.Name, w) || StartsAWord(r.Card.Look.Version, w)))
            .ToList();
        RomList.ItemsSource = visible;
        RomList.SelectedItem = visible.FirstOrDefault(r => r.Rom.Path.Equals(keep, StringComparison.OrdinalIgnoreCase)) ?? visible.FirstOrDefault();
        if (RomList.SelectedItem != null) RomList.ScrollIntoView(RomList.SelectedItem);
        SearchHint.Visibility = TxtSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoRoms.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnOpenRoms.Visibility = _roms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtNoRoms.Text = _roms.Count == 0
            ? "Leg deine Spiele in den ROM-Ordner – daraus mischt der Randomizer neue Runs."
            : "Kein Original passt zu Filter und Suche.";
    }

    static bool StartsAWord(string text, string word) =>
        text.Split([' ', '-', '_', '(', ')', '.', ','], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.StartsWith(word, StringComparison.OrdinalIgnoreCase));

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _loading = true;
        ShowRoms(null); // typing picks the best match
        _loading = false;
        UpdateGame();
    }

    /// <summary>The presets that come with MonHub plus the ones next to the chosen file (e.g. saved from the editor).</summary>
    void LoadPresets()
    {
        // the chosen preset was deleted or renamed (e.g. by an update): back to the default one
        if (!File.Exists(_cfg.SettingsFile) && HubSetup.DefaultPresetPath() is { } fallback)
        {
            _cfg.SettingsFile = fallback;
            _cfg.Save();
        }
        var folders = new[] { Path.Combine(HubPaths.AppDir, "Settings"), Path.GetDirectoryName(_cfg.SettingsFile) ?? "" }
            .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        var files = folders.SelectMany(d => Directory.GetFiles(d, "*.rnqs"))
            .Where(f => !Path.GetFileName(f).StartsWith("Haertetest", StringComparison.OrdinalIgnoreCase))
            .Append(_cfg.SettingsFile).Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(f => new Preset(f)).ToList();
        CmbPreset.ItemsSource = files;
        CmbPreset.SelectedItem = files.FirstOrDefault(p => p.Path.Equals(_cfg.SettingsFile, StringComparison.OrdinalIgnoreCase));
    }

    void Rom_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (SelectedRom is { } rom)
        {
            _cfg.LastRom = rom.Path;
            _cfg.Save();
        }
        UpdateGame();
    }

    void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbPreset.SelectedItem is not Preset preset) return;
        _cfg.SettingsFile = preset.Path;
        _cfg.Save();
    }

    void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _cfg.AskForName = ChkAskName.IsChecked == true;
        _cfg.CreateSpoilerFile = ChkSpoilerFile.IsChecked == true;
        _cfg.CreateLog = ChkFullLog.IsChecked == true;
        _cfg.Save();
    }

    void RareCandy_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _cfg.RareCandyCheat = ChkRareCandy.IsChecked == true;
        _cfg.Save();
        UpdateCheatHint();
    }

    /// <summary>Cheat hint, own cheat code and buttons for the selected game.</summary>
    void UpdateGame()
    {
        var rom = SelectedRom;
        Chosen.DataContext = RomList.SelectedItem;
        Chosen.Visibility = rom != null ? Visibility.Visible : Visibility.Collapsed;
        TxtEmuHint.Visibility = rom != null && NeedsDeSmuME(rom) ? Visibility.Visible : Visibility.Collapsed;
        _loading = true;
        TxtCustomCheat.Text = rom != null && _cfg.CustomCheats.TryGetValue(rom.GameCode, out var own) ? own : "";
        _loading = false;
        CheatExpander.Visibility = rom is { Platform: RomPlatform.Nds } && rom.GameCode.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCheatHint();
        UpdateButtons();
    }

    void UpdateCheatHint()
    {
        var (hint, available, warning) = RandoRun.CheatHint(_cfg, SelectedRom);
        // greyed out (and shown unticked) without a code; the saved choice stays for the next ROM
        _loading = true;
        ChkRareCandy.IsEnabled = available;
        ChkRareCandy.IsChecked = available && _cfg.RareCandyCheat;
        _loading = false;
        TxtCheatHint.Text = hint;
        TxtCheatHint.SetResourceReference(TextBlock.ForegroundProperty, warning ? "Hub.Danger" : "Hub.InkMuted");
        TxtCheatHint.Visibility = hint != "" && (!available || ChkRareCandy.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
    }

    void CustomCheat_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || SelectedRom is not { } rom) return;
        var text = TxtCustomCheat.Text.Trim();
        try
        {
            if (text.Length > 0) RareCandyCodes.ParseLines(text);
            TxtCustomCheatInfo.Text = text.Length > 0 ? "Eigener Code wird benutzt." : "Leer = eingebauter Code.";
            TxtCustomCheatInfo.SetResourceReference(TextBlock.ForegroundProperty, "Hub.InkMuted");
            if (text.Length > 0) _cfg.CustomCheats[rom.GameCode] = text;
            else _cfg.CustomCheats.Remove(rom.GameCode);
            _cfg.Save(); // right away: coming back to MonHub reloads the page
        }
        catch (Exception ex)
        {
            TxtCustomCheatInfo.Text = "⚠ " + ex.Message;
            TxtCustomCheatInfo.SetResourceReference(TextBlock.ForegroundProperty, "Hub.Danger");
        }
    }

    void CustomCheat_LostFocus(object sender, RoutedEventArgs e) => UpdateCheatHint();

    void UpdateButtons()
    {
        var rom = SelectedRom;
        bool canPlay = rom != null && (Rom3ds.IsFile(rom.Path) ? HubPaths.AzaharExe != null : rom.Platform != RomPlatform.Nds || EmulatorFor(rom)?.Exe != null);
        BtnRandomize.IsEnabled = !_busy && rom != null;
        BtnRandomizePlay.IsEnabled = !_busy && canPlay;
        BtnEditPreset.IsEnabled = !_busy;
        RomList.IsEnabled = OptionsPanel.IsEnabled = !_busy;
    }

    async void EditPreset_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_cfg.SettingsFile))
        {
            SetStatus("Keine Settings-Datei ausgewählt.", "Hub.Danger");
            return;
        }
        BtnEditPreset.IsEnabled = false;
        SetStatus($"Lade {Path.GetFileName(_cfg.SettingsFile)} …");
        List<RandoOption> options;
        try
        {
            options = await RandoSettingsFile.LoadAsync(_cfg, _cfg.SettingsFile);
        }
        catch (Exception ex)
        {
            SetStatus("Settings-Datei konnte nicht gelesen werden: " + ex.Message, "Hub.Danger");
            return;
        }
        finally
        {
            BtnEditPreset.IsEnabled = true;
        }
        SetStatus("");
        var dlg = new SettingsEditorWindow(_cfg, _cfg.SettingsFile, options) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || dlg.SavedPath == null) return;
        bool isNew = !dlg.SavedPath.Equals(_cfg.SettingsFile, StringComparison.OrdinalIgnoreCase);
        if (isNew)
        {
            _cfg.SettingsFile = dlg.SavedPath;
            _cfg.Save();
            _loading = true;
            LoadPresets();
            _loading = false;
        }
        SetStatus(isNew ? $"Neue Settings-Datei gespeichert und ausgewählt: {Path.GetFileName(dlg.SavedPath)}"
                        : $"{Path.GetFileName(dlg.SavedPath)} gespeichert.", "Hub.Good");
    }

    async void Randomize_Click(object sender, RoutedEventArgs e) => await RandomizeAsync();

    async void RandomizePlay_Click(object sender, RoutedEventArgs e)
    {
        if (await RandomizeAsync()) Play_Click(sender, e);
    }

    /// <summary>Creates a new randomized ROM – the same steps as der Randomizer; true when it worked.</summary>
    async Task<bool> RandomizeAsync()
    {
        var rom = SelectedRom;
        if (RandoRun.Validate(_cfg, rom) is { } error)
        {
            SetStatus(error, "Hub.Danger");
            return false;
        }

        var emu = EmulatorFor(rom!);
        Directory.CreateDirectory(_cfg.OutputFolder);
        // 3DS games come out as .cxi (the randomizer's format for them, which Azahar starts directly)
        var ext = Rom3ds.IsFile(rom!.Path) ? ".cxi" : Path.GetExtension(rom!.Path);
        var outPath = Path.Combine(_cfg.OutputFolder, RandoRun.UniqueBaseName(_cfg, RandoRun.DefaultBaseName(rom), ext, emu) + ext);
        var run = new RandoRun(_cfg, Log);

        // a run is as big as its original (3DS: 2–4 GB) – say so before the randomizer fails half way
        try
        {
            long need = new FileInfo(rom.Path).Length + (64L << 20);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(outPath))!);
            if (drive.IsReady && drive.AvailableFreeSpace < need)
            {
                SetStatus($"Nicht genug Platz auf {drive.Name}: der neue Run braucht etwa {need >> 20} MB, frei sind {drive.AvailableFreeSpace >> 20} MB.", "Hub.Danger");
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // network path or odd drive: let the randomizer try
        }

        SetBusy(true);
        ResultPanel.Visibility = Visibility.Collapsed;
        TxtLog.Clear();
        SetStatus("");
        TxtBusy.Text = $"Mische {Path.GetFileNameWithoutExtension(rom.Path)} … der Ball wackelt!";
        Log($"ROM:      {rom.Path}");
        Log($"Settings: {_cfg.SettingsFile}");
        Log($"Ausgabe:  {outPath}");
        Log("");
        try
        {
            var started = DateTime.Now;
            int exit = await run.RunRandomizer(rom.Path, outPath);
            // the ball wobbles at least once, even for fast games
            var remaining = TimeSpan.FromMilliseconds(1300) - (DateTime.Now - started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            if (exit != 0 || !File.Exists(outPath))
            {
                RemovePartial(outPath);
                SetStatus($"Oh nein, es ist entkommen! Der Randomizer ist fehlgeschlagen (Exit-Code {exit}). Details im Log.", "Hub.Danger");
                LogExpander.IsExpanded = true;
                return false;
            }

            if (_cfg.AskForName) outPath = AskAndRename(run, outPath, emu);
            var spoiler = run.Spoiler(rom, outPath);
            var cheat = ChkRareCandy.IsChecked == true ? run.ApplyCheat(rom, outPath, emu) : "";

            _lastOutput = outPath;
            _lastRom = rom;
            TxtResultName.Text = Path.GetFileNameWithoutExtension(outPath);
            TxtResultCheat.Text = cheat;
            TxtResultCheat.Visibility = cheat.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtSpoiler.Text = spoiler ?? "";
            SpoilerExpander.IsExpanded = false; // no surprises: open it only if you want to know
            SpoilerExpander.Visibility = spoiler != null ? Visibility.Visible : Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
            return true;
        }
        catch (Exception ex)
        {
            Log("FEHLER: " + ex);
            LogExpander.IsExpanded = true;
            SetStatus("Fehler: " + ex.Message, "Hub.Danger");
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>A failed randomizer can leave a cut-off ROM behind – it would show up as a broken game, so it goes.</summary>
    void RemovePartial(string outPath)
    {
        foreach (var file in new[] { outPath, outPath + ".log" })
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log($"Konnte {Path.GetFileName(file)} nicht entfernen: {ex.Message}");
            }
        }
    }

    string AskAndRename(RandoRun run, string outPath, IEmulator? emu)
    {
        var ext = Path.GetExtension(outPath);
        var current = Path.GetFileNameWithoutExtension(outPath);
        var dlg = new NameDialog(current, name => name != current && RandoRun.IsNameTaken(_cfg, name, ext, emu)) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || dlg.ChosenName == current)
        {
            Log($"Name beibehalten: {current}{ext}");
            return outPath;
        }
        return run.Rename(outPath, dlg.ChosenName);
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateButtons();
        BusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy && HubMotion.Level != MotionLevel.Off)
        {
            var wobble = new DoubleAnimation(-16, 16, TimeSpan.FromMilliseconds(320))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(wobble, 24);
            Wobble.BeginAnimation(RotateTransform.AngleProperty, wobble);
        }
        else
            Wobble.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    /// <summary>Can be called from the randomizer's output threads.</summary>
    void Log(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Log(line));
            return;
        }
        TxtLog.AppendText(line + Environment.NewLine);
        TxtLog.ScrollToEnd();
    }

    void SetStatus(string text, string brushKey = "Hub.Ink")
    {
        TxtStatus.Text = text;
        TxtStatus.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtStatus.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
    }

    /// <summary>DS runs in der Randomizer's emulator (with the cheat), everything else with the program Windows uses.</summary>
    void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_lastOutput == null || !File.Exists(_lastOutput)) return;
        var owner = Window.GetWindow(this);
        if (_lastRom?.Platform == RomPlatform.Nds && EmulatorFor(_lastRom) is { Exe: { } exe })
            Launcher.Start(owner, exe, $"\"{_lastOutput}\"");
        else
            Launcher.StartGame(owner, _lastOutput, null); // Game Boy / GBA: mGBA
    }

    static bool NeedsDeSmuME(RomInfo rom) => rom.Platform == RomPlatform.Nds && !Bios.Installed && RomCheck.HasEncryptedSecureArea(rom.Path);

    /// <summary>der Randomizer's emulator – DeSmuME for encrypted ROMs, which melonDS can't boot without a Nintendo BIOS
    /// (the cheat file is then written for DeSmuME too).</summary>
    IEmulator? EmulatorFor(RomInfo rom) =>
        NeedsDeSmuME(rom) && Emulators.TryOpen(HubPaths.DeSmuME) is { Exe: not null } desmume ? desmume : Emulators.TryOpen(_cfg.EmulatorFolder);

    void ShowFile_Click(object sender, RoutedEventArgs e)
    {
        if (_lastOutput != null && File.Exists(_lastOutput))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastOutput}\"") { UseShellExecute = true });
    }

    void Games_Click(object sender, RoutedEventArgs e) => Shell.Navigate("games", _lastOutput);
    void OpenRoms_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.Roms);

    /// <summary>Narrow window: the chosen game and its buttons go below the list; both parts scroll on their own.</summary>
    void Arrange(double width)
    {
        bool narrow = width < 760;
        SideColumn.Width = narrow ? new GridLength(0) : new GridLength(340);
        SideGap.Width = narrow ? new GridLength(0) : new GridLength(18);
        SideRow.Height = narrow ? new GridLength(1.3, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(Side, narrow ? 0 : 2);
        Grid.SetRow(Side, narrow ? 1 : 0);
        Side.Margin = narrow ? new Thickness(0, 14, 0, 0) : new Thickness(0);
    }
}
