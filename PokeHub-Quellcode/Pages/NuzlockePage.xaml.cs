using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PokeHub;

/// <summary>A player column: its name (edited in place) and whether it can be removed.</summary>
public class PlayerView(NuzlockeRun run, int index, Action changed) : INotifyPropertyChanged
{
    public int Index { get; } = index;

    public string Name
    {
        get => run.Players[Index];
        set
        {
            run.Players[Index] = value;
            changed();
        }
    }

    public Visibility RemoveVisibility => run.Players.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
}

/// <summary>
/// "Nuzlocke": per run, the places of the game in story order; per route and player the Pokémon caught there, and a
/// strike when it died or got away. Saved per ROM (System\Einstellungen\Nuzlocke\&lt;ROM&gt;.json), right after each change.
/// </summary>
public partial class NuzlockePage : UserControl, IHubPage
{
    List<GameEntry> _games = new();
    GameEntry? _game;
    NuzlockeRun? _run;
    List<RouteView> _rows = new();
    bool _loading;
    readonly DispatcherTimer _save;

    public NuzlockePage()
    {
        InitializeComponent();
        _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _save.Tick += (_, _) => SaveNow();
        Unloaded += (_, _) => SaveNow();
    }

    /// <summary>The run list is refreshed; the open tracker stays as it is (edits are saved anyway).</summary>
    int _loadId;
    string? _shown;

    public async void Refresh()
    {
        int id = ++_loadId;
        List<GameEntry> all;
        try
        {
            all = await GameData.LoadAsync(); // shared with the other pages' loads
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return;
        }
        if (id != _loadId) return;
        var signature = string.Join("|", all.Select(g => g.Rom + "*" + g.RomTime.Ticks));
        if (signature == _shown) return; // same games: the open tracker stays as it is
        _shown = signature;
        _games = all.Where(g => g.IsDS || g.IsGameBoy || g.Is3DS)
            .OrderByDescending(g => g.IsRun).ThenByDescending(g => g.NewestSave?.Time ?? g.RomTime).ToList();
        var keep = _game?.Rom ?? GameLibrary.Continue(all)?.Game.Rom;
        _loading = true;
        CmbRun.ItemsSource = _games.Select(g => $"{g.Name}  ·  {(g.IsRun ? "Run" : "Original")}").ToList();
        int at = _games.FindIndex(g => g.Rom == keep);
        CmbRun.SelectedIndex = at >= 0 ? at : _games.Count > 0 ? 0 : -1;
        _loading = false;
        if (CmbRun.SelectedIndex >= 0 && (_game == null || _games[CmbRun.SelectedIndex].Rom != _game.Rom))
            Open(_games[CmbRun.SelectedIndex]);
        else if (_games.Count == 0)
            TxtSummary.Text = "Noch keine Spiele – leg ROMs in den ROM-Ordner oder misch einen Run.";
    }

    void Run_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CmbRun.SelectedIndex < 0) return;
        Open(_games[CmbRun.SelectedIndex]);
    }

    void Open(GameEntry game)
    {
        SaveNow();
        _game = game;
        _run = NuzlockeRun.Load(game);
        Build();
    }

    void Build()
    {
        if (_run == null) return;
        PlayerList.ItemsSource = _run.Players.Select((_, i) => new PlayerView(_run, i, Changed)).ToList();
        _loading = true;
        ChkSoulLink.IsChecked = _run.SoulLink;
        _loading = false;
        _rows = _run.Routes.Select(r => new RouteView(r, r.Catches.Select(c => new CatchView(c, Changed, _run.SoulLink)).ToList(), _run.SoulLink)).ToList();
        ShowRows();
    }

    void ShowRows()
    {
        RouteList.ItemsSource = ChkOpen.IsChecked == true ? _rows.Where(r => r.Open).ToList() : _rows;
        UpdateSummary();
    }

    void UpdateSummary()
    {
        if (_run == null || _game == null) return;
        var all = _run.Routes.SelectMany(r => r.Catches).ToList();
        int caught = all.Count(c => c.Species > 0 && !c.Gone), gone = all.Count(c => c.Gone);
        var edition = NuzlockeData.VersionOf(_game) is null ? "Spiel nicht erkannt – füg deine Routen selbst hinzu" : _game.Name;
        TxtSummary.Text = $"{edition} · {_run.Routes.Count} Routen · {caught} dabei · {gone} gestrichen · {_run.Players.Count} {(_run.Players.Count == 1 ? "Spieler" : "Spieler")}";
    }

    void Changed()
    {
        UpdateSummary();
        _save.Stop();
        _save.Start();
    }

    void SaveNow()
    {
        _save.Stop();
        if (_run == null || _game == null) return;
        try
        {
            _run.Save(_game);
        }
        catch
        {
            // disk full/locked: the next change tries again
        }
    }

    void Filter_Changed(object sender, RoutedEventArgs e) => ShowRows();

    void SoulLink_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _run == null) return;
        _run.SoulLink = ChkSoulLink.IsChecked == true;
        Build();
        Changed();
    }

    void AddPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_run == null) return;
        _run.Players.Add($"Spieler {_run.Players.Count + 1}");
        foreach (var route in _run.Routes) route.Catches.Add(new NuzlockeCatch());
        Build();
        Changed();
    }

    void RemovePlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_run == null || _run.Players.Count < 2 || sender is not FrameworkElement { Tag: int index }) return;
        if (MessageBox.Show(Window.GetWindow(this), $"„{_run.Players[index]}“ mit allen Einträgen entfernen?", "Nuzlocke",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _run.Players.RemoveAt(index);
        foreach (var route in _run.Routes)
            if (index < route.Catches.Count) route.Catches.RemoveAt(index);
        Build();
        Changed();
    }

    void AddRoute_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtNewRoute.Text.Trim();
        if (_run == null || name.Length == 0) return;
        _run.Routes.Add(new NuzlockeRoute
        {
            Id = "eigen-" + Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Custom = true,
            Catches = _run.Players.Select(_ => new NuzlockeCatch()).ToList(),
        });
        TxtNewRoute.Clear();
        Build();
        Changed();
        RouteList.ScrollIntoView(_rows[^1]);
    }

    void NewRoute_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddRoute_Click(sender, e);
    }

    void RemoveRoute_Click(object sender, RoutedEventArgs e)
    {
        if (_run == null || (sender as FrameworkElement)?.DataContext is not RouteView row) return;
        _run.Routes.Remove(row.Route);
        Build();
        Changed();
    }

    /// <summary>The player names scroll sideways with the routes.</summary>
    void Routes_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0) HeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
    }
}
