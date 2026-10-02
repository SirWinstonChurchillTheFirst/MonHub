using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PokeHub;

/// <summary>The in-game "continue" screen for the newest save, plus trainer card, recent saves and a new-run nudge.</summary>
public partial class StartPage : UserControl, IHubPage
{
    /// <summary>What the continue box shows.</summary>
    public class ContinueView(GameCard card, string? emulator, bool saved)
    {
        public GameCard Card { get; } = card;
        public string? Emulator { get; } = emulator;
        public bool Saved { get; } = saved;
        SaveSummary? S => Card.Summary;

        public string Kicker => Saved ? Txt.L("WEITER", "CONTINUE") : Txt.L("NEUER RUN", "NEW RUN");
        public string Player => S?.Trainer ?? "–";
        public string Time => S?.PlayTime ?? "–";
        public string Dex => S != null ? Txt.L($"{S.DexSeen} gesehen · {S.DexCaught} gefangen", $"{S.DexSeen} seen · {S.DexCaught} caught") : "–";
        public List<bool> Badges => Enumerable.Range(0, S?.BadgeSlots ?? 8).Select(i => i < (S?.Badges ?? 0)).ToList();
        public string TeamHint => S == null
            ? Saved ? Card.Game.Family != null ? Txt.L("Den Spielstand kann MonHub nicht lesen.", "MonHub can't read this save.")
                                               : Txt.L("Team und Orden liest MonHub aus diesem Spiel noch nicht.", "MonHub doesn't read team and badges from this game yet.")
                    : Txt.L("Noch kein Team – im Spiel wählst du gleich deinen Starter.", "No team yet – you'll pick your starter in the game in a moment.")
            : S.Party.Count == 0 ? Txt.L("Noch kein Pokémon im Team.", "No Pokémon in the team yet.") : "";
        public Visibility TeamHintVisibility => TeamHint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string ButtonText => Saved ? Txt.L("▶  Weiterspielen", "▶  Continue") : Txt.L("▶  Run starten", "▶  Start run");
        public string Note
        {
            get
            {
                var where = Emulator ?? Txt.L("Standardprogramm", "Default program");
                return Saved && Card.Game.NewestSave is { } save
                    ? $"{where} · " + Txt.L($"gespeichert {TimeText.Ago(save.Time)}", $"saved {TimeText.Ago(save.Time)}")
                    : $"{where} · " + Txt.L($"erstellt {TimeText.Ago(Card.Game.RomTime)}", $"created {TimeText.Ago(Card.Game.RomTime)}");
            }
        }
    }

    public record TrainerRow(string Label, string Value);

    /// <summary>Trainer card: who you are in your newest save, and what you did across all of them.</summary>
    public class TrainerView
    {
        public string Name { get; init; } = "";
        public string IdLine { get; init; } = "";
        public List<TrainerRow> Rows { get; init; } = [];
        public string Footnote { get; init; } = "";
    }

    static readonly int[][] StarterTrios = [[1, 4, 7], [152, 155, 158], [252, 255, 258], [387, 390, 393], [495, 498, 501], [650, 653, 656], [722, 725, 728]];
    static readonly string[] Regions = ["Kanto", "Johto", "Hoenn", "Sinnoh", Txt.L("Einall", "Unova"), "Kalos", "Alola"];

    (GameEntry Game, string? Emulator, bool Saved)? _continue;

    public StartPage()
    {
        InitializeComponent();
        int trio = Random.Shared.Next(StarterTrios.Length);
        StarterRow.ItemsSource = StarterTrios[trio].Select(Species.SpriteKey).Where(k => k != null).ToList();
        TxtStarterLine.Text = Txt.L($"Mische ein Spiel neu – Starter, Trainer und wilde Pokémon. Heute schauen die Starter aus {Regions[trio]} vorbei.",
            $"Mix up a game – starters, trainers and wild Pokémon. Today the starters from {Regions[trio]} drop by.");
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    int _loadId;
    string? _shown;

    /// <summary>Reads the library in the background; the page is rebuilt only when something changed.</summary>
    public async void Refresh()
    {
        int id = ++_loadId;
        List<GameEntry> games;
        try
        {
            games = await GameData.LoadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex); // unreadable right now: keep what is shown
            return;
        }
        if (id != _loadId) return; // a newer refresh is on its way
        var signature = GameData.Signature(games) + "|" + HubConfig.Current.TrainerName;
        if (signature == _shown) return;
        _shown = signature;
        Show(games);
    }

    void Show(List<GameEntry> games)
    {
        var cards = games.Select(g => new GameCard(g)).ToList();
        _continue = GameLibrary.Continue(games);

        Onboarding.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Hero.Visibility = _continue != null ? Visibility.Visible : Visibility.Collapsed;

        var withSummary = cards.Where(c => c.Summary != null).ToList();
        var name = HubConfig.Current.TrainerName is { Length: > 0 } own ? own
                 : _continue is { } c0 && new GameCard(c0.Game).Summary is { } s0 ? s0.Trainer
                 : withSummary.FirstOrDefault()?.Summary?.Trainer ?? "Trainer";

        TxtKicker.Text = _continue is { Saved: true } ? Txt.L("WEITER", "CONTINUE") : games.Count == 0 ? Txt.L("WILLKOMMEN", "WELCOME") : Txt.L("BEREIT", "READY");
        TxtGreeting.Text = _continue is { Saved: true } ? Txt.L($"Willkommen zurück, {name}!", $"Welcome back, {name}!")
                         : games.Count > 0 ? Txt.L($"Hallo, {name}!", $"Hi, {name}!") : Txt.L("Willkommen im MonHub!", "Welcome to MonHub!");
        TxtSubline.Text = _continue switch
        {
            { Saved: true } => Txt.L("Dein letztes Abenteuer wartet – genau da, wo du gespeichert hast.", "Your last adventure is waiting – right where you saved."),
            { } => Txt.L("Dein neuer Run ist bereit.", "Your new run is ready."),
            null when games.Count > 0 => Txt.L("Such dir unter „Spiele“ etwas aus oder erstelle einen neuen Run.", "Pick something under “Games” or create a new run."),
            _ => "",
        };

        if (_continue is { } c)
        {
            var card = new GameCard(c.Game);
            ContinuePanel.DataContext = new ContinueView(card, c.Emulator, c.Saved);
            TrainerPanel.DataContext = BuildTrainer(name, card, cards, withSummary);
        }

        var recent = cards.Where(x => x.HasSave && x.Game.Rom != _continue?.Game.Rom).Take(4).ToList();
        RecentList.ItemsSource = recent;
        RecentSection.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static TrainerView BuildTrainer(string name, GameCard current, List<GameCard> all, List<GameCard> withSummary)
    {
        var total = withSummary.Aggregate(TimeSpan.Zero, (sum, g) => sum + g.Summary!.PlayTimeSpan);
        var rows = new List<TrainerRow>();
        if (current.Summary is { } s) rows.Add(new(Txt.L("GELD", "MONEY"), $"₽ {s.Money:N0}"));
        rows.Add(new(Txt.L("SPIELZEIT GESAMT", "TOTAL PLAY TIME"), total.TotalHours >= 1
            ? Txt.L($"{(int)total.TotalHours} Std. {total.Minutes} Min.", $"{(int)total.TotalHours} h {total.Minutes} min")
            : Txt.L($"{total.Minutes} Min.", $"{total.Minutes} min")));
        rows.Add(new(Txt.L("ORDEN GESAMT", "TOTAL BADGES"), withSummary.Sum(g => g.Summary!.Badges).ToString()));
        rows.Add(new(Txt.L("ABENTEUER", "ADVENTURES"), withSummary.Count.ToString()));
        rows.Add(new(Txt.L("RUNS ERSTELLT", "RUNS CREATED"), all.Count(g => g.Game.IsRun).ToString()));
        return new TrainerView
        {
            Name = name,
            IdLine = current.Summary is { } cs ? (Txt.L("ID-Nr. ", "ID No. ") + $"{cs.TrainerId:00000} · {current.Look.Version}").TrimEnd(' ', '·')
                : Txt.L("Noch ohne Spielstand", "No save yet"),
            Rows = rows,
            Footnote = withSummary.Count > 0 ? Txt.L("Aus deinen Spielständen gelesen.", "Read from your saves.") : "",
        };
    }

    /// <summary>Narrow window: the trainer card goes below the continue box.</summary>
    void Arrange(double width)
    {
        bool narrow = width < 900;
        TrainerColumn.Width = narrow ? new GridLength(0) : new GridLength(292);
        TrainerGap.Width = narrow ? new GridLength(0) : new GridLength(20);
        Grid.SetColumn(TrainerPanel, narrow ? 0 : 2);
        Grid.SetRow(TrainerPanel, narrow ? 1 : 0);
        TrainerPanel.Margin = narrow ? new Thickness(0, 18, 0, 0) : new Thickness(0, 14, 0, 0);
        TrainerPanel.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        TrainerPanel.MinWidth = narrow ? 320 : 0;
    }

    void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_continue is { } c) Launcher.StartGame(Window.GetWindow(this), c.Game.Rom, c.Emulator);
    }

    /// <summary>A recent save: straight into the game, in the emulator that holds the save.</summary>
    void Recent_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecentList.SelectedItem is not GameCard card) return;
        RecentList.SelectedItem = null;
        Launcher.StartGame(Window.GetWindow(this), card.Game.Rom, card.Game.NewestSave?.Emulator ?? GameLibrary.EmulatorFor(card.Game));
    }

    void OtherGame_Click(object sender, RoutedEventArgs e) => Shell.Navigate("games");
    void PokeRando_Click(object sender, RoutedEventArgs e) => Shell.Navigate("run");
    void NeuerRun_Click(object sender, RoutedEventArgs e) => Shell.Navigate("run");
    void OpenRoms_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.Roms);

    void Import_Click(object sender, RoutedEventArgs e)
    {
        new ImportWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        Refresh();
    }

    /// <summary>Lists inside the page scroll the page, not themselves.</summary>
    void List_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => Shell.ForwardWheel(sender, e);
}
