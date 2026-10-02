using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PokeHub;

/// <summary>
/// "Spiele": every game and run. Each opens in the emulator that holds its newest save; picking the other one takes the
/// save along (melonDS .sav ⇄ DeSmuME .dsv). Favourites stay on top.
/// </summary>
public partial class LibraryPage : UserControl, IHubPage
{
    List<GameCard> _cards = new();
    bool _ready, _updating;
    string? _pendingSelect;

    public LibraryPage()
    {
        InitializeComponent();
        MelonIcon.Source = IconHelper.Get(HubPaths.MelonDSExe);
        DesmumeIcon.Source = IconHelper.Get(HubPaths.DeSmuMEExe);
        MgbaIcon.Source = IconHelper.Get(HubPaths.MGBAExe);
        AzaharIcon.Source = IconHelper.Get(HubPaths.AzaharExe);
        _ready = true;
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    /// <summary>Select this ROM on the next refresh (e.g. coming from the start page).</summary>
    public void Select(string? rom) => _pendingSelect = rom;

    public void Refresh() => Reload(_pendingSelect ?? SelectedGame?.Rom);

    GameCard? Selected => GameList.SelectedItem as GameCard;
    GameEntry? SelectedGame => Selected?.Game;

    /// <summary>melonDS / DeSmuME for DS games, null = the program Windows uses (GBA and others).</summary>
    string? ChosenEmulator => SelectedGame is { IsDS: true }
        ? RbDeSmuME.IsChecked == true ? GameLibrary.DeSmuME : GameLibrary.MelonDS
        : RbMGBA.IsChecked == true ? GameLibrary.MGBA
        : RbAzahar.IsChecked == true ? GameLibrary.Azahar : null;

    int _loadId;
    string? _shown;

    /// <summary>
    /// Reads games and orphaned saves in the background, then shows them – rebuilt only when something changed
    /// (games, saves, favourites) or another game is to be selected.
    /// </summary>
    async void Reload(string? keepRom)
    {
        _pendingSelect = null;
        int id = ++_loadId;
        List<GameEntry> games;
        int orphans;
        try
        {
            (games, orphans) = await Task.Run(async () => (await GameData.LoadAsync(), GameLibrary.OrphanSaves().Count));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex); // unreadable right now: keep what is shown
            return;
        }
        if (id != _loadId) return;
        var signature = GameData.Signature(games) + "|" + string.Join(",", HubConfig.Current.Favorites) + "|" + orphans;
        if (signature == _shown && (keepRom == null || keepRom == SelectedGame?.Rom)) return;
        _shown = signature;
        Show(games, orphans, keepRom);
    }

    void Show(List<GameEntry> games, int orphans, string? keepRom)
    {
        _cards = games.Select(g => new GameCard(g)).ToList();
        int runs = _cards.Count(c => c.Game.IsRun), saved = _cards.Count(c => c.HasSave), favorites = _cards.Count(c => c.IsFavorite);
        TxtCounts.Text = _cards.Count == 0 ? Txt.L("Noch leer.", "Empty so far.")
            : Txt.L($"{_cards.Count} {(_cards.Count == 1 ? "Spiel" : "Spiele")}, davon {runs} {(runs == 1 ? "Run" : "Runs")} · {saved} mit Spielstand",
                    $"{_cards.Count} {(_cards.Count == 1 ? "game" : "games")}, {runs} of them {(runs == 1 ? "a run" : "runs")} · {saved} with a save");
        TxtFilterAll.Text = Txt.L($"Alle  {_cards.Count}", $"All  {_cards.Count}");
        TxtFilterFavorites.Text = Txt.L($"★ Favoriten  {favorites}", $"★ Favorites  {favorites}");
        TxtFilterSaved.Text = Txt.L($"Mit Spielstand  {saved}", $"With save  {saved}");
        TxtFilterRuns.Text = $"Runs  {runs}";
        TxtFilterOriginals.Text = Txt.L($"Originale  {_cards.Count - runs}", $"Originals  {_cards.Count - runs}");
        TxtOrphans.Text = Txt.L($"{orphans} {(orphans == 1 ? "Spielstand" : "Spielstände")} ohne Spiel", $"{orphans} {(orphans == 1 ? "save" : "saves")} without a game");
        BtnOrphans.Visibility = orphans > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowGames(keepRom);
    }

    /// <summary>The cards for the current filter and search (favourites first); keeps the selection when possible.</summary>
    void ShowGames(string? keepRom)
    {
        var words = TxtSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var visible = _cards.Where(c => MatchesFilter(c) && words.All(w =>
                c.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || c.Look.Version.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.IsFavorite).ToList(); // stable: the library order stays inside both groups
        GameList.ItemsSource = visible;
        GameList.SelectedItem = visible.FirstOrDefault(c => c.Game.Rom == keepRom) ?? visible.FirstOrDefault();
        if (GameList.SelectedItem != null) GameList.ScrollIntoView(GameList.SelectedItem);

        SearchHint.Visibility = TxtSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyActions.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtEmpty.Text = _cards.Count == 0
            ? Txt.L("Noch keine Spiele. Leg ROMs in den Ordner „ROMs“ oder hol sie mit „Importieren“ von deinem alten Emulator.",
                    "No games yet. Put ROMs into the “ROMs” folder or bring them over from your old emulator with “Import”.")
            : FilterFavorites.IsChecked == true ? Txt.L("Noch keine Favoriten – markier ein Spiel mit dem ☆.", "No favorites yet – mark a game with the ☆.")
            : Txt.L("Hier ist nichts – anderer Filter oder Suchbegriff?", "Nothing here – another filter or search?");
        UpdateChoice();
    }

    bool MatchesFilter(GameCard card) =>
        FilterFavorites.IsChecked == true ? card.IsFavorite
        : FilterSaved.IsChecked == true ? card.HasSave
        : FilterRuns.IsChecked == true ? card.Game.IsRun
        : FilterOriginals.IsChecked != true || !card.Game.IsRun;

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready) ShowGames(SelectedGame?.Rom);
    }

    void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) ShowGames(SelectedGame?.Rom);
    }

    void Game_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateChoice();

    void Game_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is GameCard) Play_Click(sender, e);
    }

    void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameCard card) return;
        HubConfig.Current.ToggleFavorite(card.Game.Rom);
        Reload(SelectedGame?.Rom);
    }

    void FavoriteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is not { } game) return;
        HubConfig.Current.ToggleFavorite(game.Rom);
        Reload(game.Rom);
    }

    /// <summary>Emulator choice for the selected game, preselected where its newest save is.</summary>
    void UpdateChoice()
    {
        _updating = true;
        var game = SelectedGame;
        Detail.DataContext = Selected;
        DetailScroll.Visibility = game != null ? Visibility.Visible : Visibility.Hidden;
        bool ds = game is { IsDS: true };
        RbMelonDS.Visibility = ds && HubPaths.MelonDSExe != null ? Visibility.Visible : Visibility.Collapsed;
        RbMelonDS.IsEnabled = !(game?.NeedsDeSmuME == true && HubPaths.DeSmuMEExe != null);
        RbDeSmuME.Visibility = ds && HubPaths.DeSmuMEExe != null ? Visibility.Visible : Visibility.Collapsed;
        bool mgba = game is { IsGameBoy: true } && HubPaths.MGBAExe != null;
        RbMGBA.Visibility = mgba ? Visibility.Visible : Visibility.Collapsed;
        bool azahar = game is { Is3DS: true } && HubPaths.AzaharExe != null;
        RbAzahar.Visibility = azahar ? Visibility.Visible : Visibility.Collapsed;
        RbDefault.Visibility = game != null && !ds && !azahar ? Visibility.Visible : Visibility.Collapsed;
        if (ds)
        {
            var preferred = game!.NeedsDeSmuME && HubPaths.DeSmuMEExe != null ? GameLibrary.DeSmuME : game.NewestSave?.Emulator ?? GameLibrary.EmulatorFor(game);
            (preferred == GameLibrary.DeSmuME && RbDeSmuME.Visibility == Visibility.Visible ? RbDeSmuME : RbMelonDS).IsChecked = true;
        }
        else if (game != null)
            (mgba ? RbMGBA : azahar ? RbAzahar : RbDefault).IsChecked = true;
        ChkTransfer.IsChecked = true;
        _updating = false;
        UpdateHint();
    }

    void Emulator_Changed(object sender, RoutedEventArgs e)
    {
        if (!_updating && _ready) UpdateHint();
    }

    void Transfer_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updating && _ready) UpdateHint();
    }

    /// <summary>Tells what will happen with the save for the chosen emulator.</summary>
    void UpdateHint()
    {
        var game = SelectedGame;
        BtnPlay.IsEnabled = BtnShowFile.IsEnabled = BtnDeleteGame.IsEnabled = game != null;
        BtnDeleteSaves.IsEnabled = MenuDeleteSaves.IsEnabled = game?.NewestSave != null;
        ChkTransfer.Visibility = Visibility.Collapsed;
        if (game == null)
        {
            TxtHint.Text = "";
            return;
        }
        if (game.Is3DS && ChosenEmulator == GameLibrary.Azahar)
        {
            TxtHint.Text = Rom3ds.Read(game.Rom) is { Encrypted: true }
                ? Txt.L("Dieses 3DS-Spiel ist noch verschlüsselt – Azahar startet nur entschlüsselte Spiele.", "This 3DS game is still encrypted – Azahar only starts decrypted games.")
                : game.AzaharSave is { } n3dsSave ? SavedIn(n3dsSave.Time, "Azahar") : NoSaveYet;
            return;
        }
        if (!game.IsDS)
        {
            TxtHint.Text = ChosenEmulator == GameLibrary.MGBA
                ? game.MGBASave is { } gbSave ? SavedIn(gbSave.Time, "mGBA") : NoSaveYet
                : Txt.L("Öffnet mit dem Programm, das Windows für diese Datei benutzt.", "Opens with the program Windows uses for this file.");
            return;
        }

        var emulator = ChosenEmulator ?? GameLibrary.MelonDS;
        var newest = game.NewestSave;
        if (game.NeedsDeSmuME && !RbMelonDS.IsEnabled)
        {
            TxtHint.Text = Txt.L("Diese ROM ist verschlüsselt (typisch für US-Versionen) – melonDS bräuchte dafür ein Nintendo-BIOS, deshalb startet sie in DeSmuME.",
                "This ROM is encrypted (typical for US versions) – melonDS would need a Nintendo BIOS for it, so it starts in DeSmuME.");
            return;
        }
        var own = game.SaveOf(emulator);
        if (newest == null)
            TxtHint.Text = NoSaveYet;
        else if (newest.Emulator == emulator)
            TxtHint.Text = SavedIn(newest.Time, emulator);
        else
        {
            // the newest save is in the other emulator
            bool possible = GameLibrary.CanTransfer(game, emulator);
            ChkTransfer.Visibility = Visibility.Visible;
            ChkTransfer.IsEnabled = possible;
            ChkTransfer.Content = Txt.L($"Spielstand aus {newest.Emulator} mitnehmen ({TimeText.Ago(newest.Time)})",
                $"Take the save from {newest.Emulator} along ({TimeText.Ago(newest.Time)})");
            if (!possible)
                TxtHint.Text = own != null
                    ? Txt.L($"Dieser Spielstand lässt sich nicht nach {emulator} übernehmen – {emulator} startet mit seinem älteren Spielstand ({TimeText.Ago(own.Time)}).",
                            $"This save can't be taken over to {emulator} – {emulator} starts with its older save ({TimeText.Ago(own.Time)}).")
                    : Txt.L($"Dieser Spielstand lässt sich nicht nach {emulator} übernehmen – {emulator} startet ohne Spielstand.",
                            $"This save can't be taken over to {emulator} – {emulator} starts without a save.");
            else if (ChkTransfer.IsChecked == true)
                TxtHint.Text = own != null
                    ? Txt.L($"Dein älterer {emulator}-Spielstand ({TimeText.Ago(own.Time)}) kommt dabei in den Papierkorb.",
                            $"Your older {emulator} save ({TimeText.Ago(own.Time)}) goes to the Recycle Bin.")
                    : Txt.L($"Der Spielstand wird für {emulator} umgewandelt – der aus {newest.Emulator} bleibt auch da.",
                            $"The save is converted for {emulator} – the one from {newest.Emulator} stays too.");
            else
                TxtHint.Text = own != null
                    ? Txt.L($"{emulator} startet mit seinem älteren Spielstand ({TimeText.Ago(own.Time)}).", $"{emulator} starts with its older save ({TimeText.Ago(own.Time)}).")
                    : Txt.L($"{emulator} hat keinen Spielstand – das Spiel startet von vorn.", $"{emulator} has no save – the game starts from the beginning.");
        }
    }

    static string NoSaveYet => Txt.L("Noch kein Spielstand – das Spiel startet von vorn.", "No save yet – the game starts from the beginning.");

    static string SavedIn(DateTime time, string emulator) =>
        Txt.L($"Gespeichert {TimeText.Ago(time)} in {emulator}.", $"Saved {TimeText.Ago(time)} in {emulator}.");

    void Play_Click(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        if (game == null) return;
        var owner = Window.GetWindow(this);
        var emulator = ChosenEmulator;
        if (emulator != null && ChkTransfer.Visibility == Visibility.Visible && ChkTransfer.IsEnabled && ChkTransfer.IsChecked == true)
        {
            if (Launcher.IsRunning(emulator))
            {
                MessageBox.Show(owner, Txt.L($"{emulator} läuft noch. Bitte speichere und schließe es zuerst – sonst kann es den Spielstand beim Beenden überschreiben.",
                    $"{emulator} is still running. Please save and close it first – otherwise it can overwrite the save when it closes."),
                    Txt.L("Spielen", "Play"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (GameLibrary.TransferSave(game, emulator) is { } error)
            {
                MessageBox.Show(owner, error, Txt.L("Spielen", "Play"), MessageBoxButton.OK, MessageBoxImage.Warning);
                Reload(game.Rom);
                return;
            }
        }
        if (emulator == null) Launcher.Start(owner, game.Rom); // "Standardprogramm" chosen on purpose
        else Launcher.StartGame(owner, game.Rom, emulator);
    }

    void ShowFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame is { } game && File.Exists(game.Rom))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{game.Rom}\"") { UseShellExecute = true });
    }

    void DeleteSaves_Click(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        var owner = Window.GetWindow(this);
        var title = Txt.L("Spielstand löschen", "Delete save");
        if (game?.NewestSave == null || Launcher.EmulatorsOpen(owner, title)) return;
        if (MessageBox.Show(owner, Txt.L($"Alle Spielstände von „{game.Name}“ löschen?\n\nSie kommen in den Papierkorb (alle Emulatoren und Savestates) – von dort kannst du sie zurückholen.",
                    $"Delete all saves of “{game.Name}”?\n\nThey go to the Recycle Bin (all emulators and savestates) – you can restore them from there."),
                title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        if (!GameLibrary.DeleteSaves(game))
            MessageBox.Show(owner, Txt.L("Nicht alle Spielstände konnten gelöscht werden.", "Not all saves could be deleted."), title, MessageBoxButton.OK, MessageBoxImage.Warning);
        Reload(game.Rom);
    }

    /// <summary>The ROM with everything that belongs to it: saves, savestates, cheats, spoiler – all into the recycle bin.</summary>
    void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        var owner = Window.GetWindow(this);
        var title = Txt.L("Spiel löschen", "Delete game");
        if (game == null || Launcher.EmulatorsOpen(owner, title)) return;

        var files = GameLibrary.FilesOf(game);
        int Count(GameLibrary.FileRole role) => files.Count(f => f.Role == role);
        var lines = new List<string> { Txt.L($"•  das Spiel ({SizeText(game.Rom)})", $"•  the game ({SizeText(game.Rom)})") };
        if (Count(GameLibrary.FileRole.Save) is > 0 and var saves)
            lines.Add(saves == 1 ? Txt.L("•  1 Spielstand", "•  1 save") : Txt.L($"•  {saves} Spielstände", $"•  {saves} saves"));
        if (Count(GameLibrary.FileRole.Savestate) is > 0 and var states) lines.Add(states == 1 ? "•  1 Savestate" : $"•  {states} Savestates");
        if (Count(GameLibrary.FileRole.Cheats) is > 0 and var cheats)
            lines.Add(cheats == 1 ? Txt.L("•  die Cheats", "•  the cheats") : Txt.L($"•  die Cheats ({cheats} Dateien)", $"•  the cheats ({cheats} files)"));
        if (Count(GameLibrary.FileRole.Spoiler) > 0) lines.Add(Txt.L("•  Spoiler und Log vom Randomizer", "•  spoiler and log from the randomizer"));
        if (Count(GameLibrary.FileRole.Tracker) > 0) lines.Add(Txt.L("•  der Nuzlocke-Tracker", "•  the Nuzlocke tracker"));
        var original = game.IsRun ? "" : Txt.L("\n\nAchtung: Das ist ein Original-Spiel – daraus mischt der Randomizer neue Runs.",
            "\n\nCareful: this is an original game – the randomizer mixes new runs from it.");
        var list = string.Join("\n", lines);

        if (MessageBox.Show(owner, Txt.L($"„{game.Name}“ löschen?\n\nIn den Papierkorb kommen:\n{list}{original}\n\nVom Papierkorb aus kannst du alles zurückholen.",
                    $"Delete “{game.Name}”?\n\nThese go to the Recycle Bin:\n{list}{original}\n\nYou can restore everything from the Recycle Bin."),
                title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        if (!GameLibrary.DeleteGame(game))
            MessageBox.Show(owner, Txt.L("Nicht alles konnte gelöscht werden – ist die Datei noch irgendwo geöffnet?", "Not everything could be deleted – is the file still open somewhere?"),
                title, MessageBoxButton.OK, MessageBoxImage.Warning);
        Reload(null);
    }

    static string SizeText(string file)
    {
        long size;
        try { size = new FileInfo(file).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "?"; }
        return size >= 1 << 20 ? $"{size / (1 << 20)} MB" : $"{Math.Max(1, size / 1024)} KB";
    }

    /// <summary>Saves whose ROM is gone – shown and cleaned up in their own window.</summary>
    void Orphans_Click(object sender, RoutedEventArgs e)
    {
        new OrphanWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        Reload(SelectedGame?.Rom);
    }

    void Folders_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    void Import_Click(object sender, RoutedEventArgs e)
    {
        new ImportWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        Reload(SelectedGame?.Rom);
    }

    void OpenRoms_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.Roms);
    void OpenRuns_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.Randomized);
    void OpenSaves_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.Saves);

    /// <summary>Narrow window: details below the list instead of beside it.</summary>
    void Arrange(double width)
    {
        bool narrow = width < 760;
        DetailColumn.Width = narrow ? new GridLength(0) : new GridLength(width < 900 ? 300 : 330);
        DetailGap.Width = narrow ? new GridLength(0) : new GridLength(18);
        DetailRow.Height = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(DetailScroll, narrow ? 0 : 2);
        Grid.SetRow(DetailScroll, narrow ? 1 : 0);
        DetailScroll.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(0);
    }
}
