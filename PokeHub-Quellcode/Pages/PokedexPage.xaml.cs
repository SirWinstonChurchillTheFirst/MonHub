using System.Windows;
using System.Windows.Controls;

namespace PokeHub;

/// <summary>One Pokémon in the Dex grid: caught, only seen, or still unknown (number and "?" only).</summary>
public class DexCell(int number, bool seen, bool caught)
{
    public const double Width = 96, Height = 104;
    public const double Step = Width + 8; // with the gap to the next cell

    public int Number { get; } = number;
    public bool Seen { get; } = seen;
    public bool Caught { get; } = caught;
    public string FullName { get; } = Species.Name(number);

    public string NumberText => $"#{Number:000}";
    public string Name => Seen ? FullName : "???";
    public string? Sprite => Seen ? Species.SpriteKey(Number) : null;
    public Visibility BallVisibility => Caught ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UnknownVisibility => Seen && Sprite != null ? Visibility.Collapsed : Visibility.Visible;
    /// <summary>Seen, not caught: a little faded, like the games show it.</summary>
    public double Fade => Caught || !Seen ? 1 : 0.6;
    public string Tooltip => Caught ? $"{NumberText} {FullName} · gefangen" : Seen ? $"{NumberText} {FullName} · gesehen" : $"{NumberText} · noch nicht gesehen";
}

/// <summary>
/// "Dex": the Dex of one save at a time, read from the save file itself (every save has its own – a run, the
/// original, a friend's game). The list follows the saves: a new save in the emulator shows up the next time the page
/// is shown or the files change.
/// </summary>
public partial class PokedexPage : UserControl, IHubPage
{
    List<GameEntry> _games = [];
    GameEntry? _game;
    List<DexCell> _cells = [];
    int _columns;
    int _loadId;
    string? _shown;
    string? _pendingSelect;
    bool _loading;

    public PokedexPage()
    {
        InitializeComponent();
    }

    /// <summary>Opens the Dex of this game (by ROM path) the next time the page fills.</summary>
    public void Select(string rom)
    {
        _pendingSelect = rom;
        Refresh();
    }

    public async void Refresh()
    {
        int id = ++_loadId;
        List<GameEntry> all;
        try
        {
            all = await GameData.LoadAsync(); // saves are read in the background
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex); // unreadable right now: keep what is shown
            return;
        }
        if (id != _loadId) return;

        var readable = all.Where(g => g.Summary != null).OrderByDescending(g => g.NewestSave!.Time).ToList();
        var signature = string.Join("|", readable.Select(g => $"{g.Rom}*{g.NewestSave!.Path}*{g.NewestSave.Time.Ticks}"));
        var keep = _pendingSelect ?? _game?.Rom ?? GameLibrary.Continue(all)?.Game.Rom;
        _pendingSelect = null;
        if (signature == _shown && keep == _game?.Rom) return; // same saves: the open Dex stays as it is
        _shown = signature;
        _games = readable;

        bool empty = _games.Count == 0;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        FilterBar.Visibility = ProgressRow.Visibility = DexRows.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (empty)
        {
            _game = null;
            bool unreadable = all.Any(g => g.NewestSave != null);
            TxtSummary.Text = "Noch kein Spielstand mit Dex.";
            TxtEmpty.Text = unreadable
                ? "Hier erscheint der Dex deiner Spielstände – aus diesen Spielen kann MonHub ihn nicht lesen (ROM-Hacks, japanische Versionen)."
                : "Spiel ein Pokémon-Spiel und speichere einmal – dann steht hier, welche Pokémon du gesehen und gefangen hast.";
            return;
        }

        _loading = true;
        CmbSave.ItemsSource = _games.Select(g => $"{g.Name}  ·  {g.Summary!.Trainer}").ToList();
        int at = _games.FindIndex(g => g.Rom == keep);
        CmbSave.SelectedIndex = at >= 0 ? at : 0;
        _loading = false;
        Open(_games[CmbSave.SelectedIndex]);
    }

    void Save_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbSave.SelectedIndex < 0 || CmbSave.SelectedIndex >= _games.Count) return;
        Open(_games[CmbSave.SelectedIndex]);
    }

    void Open(GameEntry game)
    {
        _game = game;
        var summary = game.Summary!;
        var dex = summary.Dex;
        _cells = Enumerable.Range(1, dex.Size).Select(n => new DexCell(n, dex.Seen(n), dex.Caught(n))).ToList();
        int caught = _cells.Count(c => c.Caught), seen = _cells.Count(c => c.Seen);

        var look = GameLook.For(game);
        var edition = look.Version.Length > 0 ? look.Version : game.Name;
        TxtSummary.Text = $"{summary.Trainer} · {edition} · {summary.PlayTime} Std. · {seen} gesehen · {caught} gefangen";
        TxtFilterAll.Text = $"Alle  {dex.Size}";
        TxtFilterCaught.Text = $"Gefangen  {caught}";
        TxtFilterSeen.Text = $"Nur gesehen  {seen - caught}";
        TxtFilterMissing.Text = $"Fehlen  {dex.Size - caught}";
        Progress.Value = dex.Size == 0 ? 0 : (double)caught / dex.Size;
        TxtProgress.Text = $"{caught} von {dex.Size} gefangen ({100.0 * caught / Math.Max(1, dex.Size):0} %)";
        ShowCells(scrollTop: true);
    }

    /// <summary>The cells for filter and search, cut into rows that fill the width.</summary>
    void ShowCells(bool scrollTop)
    {
        var words = TxtSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var visible = _cells.Where(c => Matches(c) && words.All(w => Hit(c, w))).ToList();
        SearchHint.Visibility = TxtSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        _columns = Columns();
        var rows = new List<List<DexCell>>();
        for (int i = 0; i < visible.Count; i += _columns) rows.Add(visible.GetRange(i, Math.Min(_columns, visible.Count - i)));
        DexRows.ItemsSource = rows;
        if (scrollTop && rows.Count > 0) DexRows.ScrollIntoView(rows[0]);
    }

    bool Matches(DexCell cell) =>
        FilterCaught.IsChecked == true ? cell.Caught
        : FilterSeen.IsChecked == true ? cell.Seen && !cell.Caught
        : FilterMissing.IsChecked == true ? !cell.Caught
        : true;

    /// <summary>"25", "#025" or a part of the name – unknown Pokémon are only found by number (no spoilers).</summary>
    static bool Hit(DexCell cell, string word)
    {
        var number = word.TrimStart('#');
        if (number.Length > 0 && number.All(char.IsAsciiDigit)) return int.TryParse(number, out int n) && n == cell.Number;
        return cell.Seen && cell.FullName.Contains(word, StringComparison.OrdinalIgnoreCase);
    }

    int Columns() => Math.Max(1, (int)((DexRows.ActualWidth - 18) / DexCell.Step)); // room for the scroll bar

    void Rows_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && _cells.Count > 0 && Columns() != _columns) ShowCells(scrollTop: false);
    }

    void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ShowCells(scrollTop: true);
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) ShowCells(scrollTop: true);
    }
}
