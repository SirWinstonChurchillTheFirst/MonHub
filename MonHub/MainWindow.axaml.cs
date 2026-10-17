using System.Reflection;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Shapes;
using Avalonia.LogicalTree;
using Avalonia.Styling;

namespace MonHub;

/// <summary>
/// The shell: menu and page host. Pages are created on first visit and kept (switching back costs nothing), and the page
/// on screen is refreshed when it is shown and whenever MonHub gets the focus back – a save written by the emulator
/// in the meantime shows up right away.
/// </summary>
public partial class MainWindow : HubWindow
{
    readonly Dictionary<string, Control> _pages = new();
    string? _current;
    bool _closed, _activatedOnce;

    public MainWindow()
    {
        InitializeComponent();
        RandoApp.WindowFit.Apply(this);
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TxtVersion.Text = version != null ? $"v{version.Major}.{version.Minor}" : "";

        HookTitleBar(TitleBar);
        HookResize(ResizeGrips);
        Activated += Window_Activated;
        Deactivated += Window_Deactivated;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) Window_StateChanged();
            // maximized, Windows puts the frame outside the screen – the content moves in by that much
            if (e.Property == OffScreenMarginProperty) Root.Margin = OffScreenMargin;
        };
        Opened += (_, _) => Window_StateChanged(); // may open maximized (last size)

        HubThemes.Changed += ApplyThemeText;
        Closed += (_, _) =>
        {
            _closed = true;
            HubThemes.Changed -= ApplyThemeText;
            foreach (var watcher in _watchers) watcher.Dispose();
        };
        // the theme detail at the bottom of the menu only when there is room for it
        SizeChanged += (_, e) => RailDeco.IsVisible = e.NewSize.Height >= 720;
        ApplyThemeText();
        Navigate("start");
        WatchFolders();

        BuildMiniMenu();
        Game.Ended += () => GameLeft();
        Game.Released += () => GameLeft();
        Game.MenuClicked += () => SetMenuOpen(!_menuOpen);
        Game.FullScreenClicked += () => SetFullScreen(!_fullScreen);
        Game.ArrangementChosen += () => ArrangementChanged(Game.Emulator == GameLibrary.Azahar);
        LayoutUpdated += (_, _) => PlaceGame();
        // a game inside MonHub would go down with the window: it goes back to its own window and runs on
        Closing += (_, e) =>
        {
            if (Game.Running && !_closeDecided && !App.ShuttingDown)
            {
                e.Cancel = true; // asked first (after this event: a dialog inside it would block the closing)
                Dispatcher.UIThread.Post(AskClose);
                return;
            }
            Game.Release(toDesktop: true);
        };
    }

    // ---------------- a game inside MonHub ----------------

    bool _menuOpen = true, _fullScreen;
    WindowState _beforeFullScreen;

    /// <summary>The ROM running inside MonHub, or null.</summary>
    public string? GameRom => Game.Running ? Game.Rom ?? "" : null;

    /// <summary>Width to height of the room a game has inside MonHub right now (below its bar).</summary>
    public double GameAreaRatio => PageHost.Bounds.Height > 120 ? PageHost.Bounds.Width / (PageHost.Bounds.Height - 62) : 16.0 / 10;

    /// <summary>A game was just started: its window comes onto the "Weiter" page (<paramref name="ratio"/>: its picture's proportions).</summary>
    public void Embed(System.Diagnostics.Process process, string rom, string? emulator, double? ratio = null)
    {
        TourLayer.Later();
        Game.Play(process, rom, emulator, ratio);
        NavPlaying.IsVisible = true;
        Navigate("start");
    }

    /// <summary>The game was closed or went to its own window: the menu comes back, "Weiter" shows the page again.</summary>
    void GameLeft()
    {
        if (_closeAfterGame)
        {
            // "Spiel beenden" when closing MonHub: the game has closed – now MonHub
            _closeDecided = true;
            Dispatcher.UIThread.Post(Close);
            return;
        }
        if (_restart is { } again)
        {
            // the emulator closed for its new screen arrangement (or for another game): that game now
            _restart = null;
            Dispatcher.UIThread.Post(() => MonHub.Launcher.StartGame(this, again.Rom, again.Emulator), DispatcherPriority.Background);
        }
        NavPlaying.IsVisible = false;
        SetFullScreen(false);
        SetMenuOpen(true);
        HubSetup.EnsurePending(); // the emulator's settings MonHub had to leave alone while it ran
        if (_current == "start") Show("start");
        else Game.IsVisible = false;
    }

    (string Rom, string? Emulator)? _restart;
    bool _closeDecided, _closeAfterGame;

    /// <summary>Closing MonHub while a game runs: let it run on in its own window, quit it, or don't close.</summary>
    void AskClose()
    {
        if (!Game.Running)
        {
            Close();
            return;
        }
        var name = System.IO.Path.GetFileNameWithoutExtension(Game.Rom ?? "");
        int choice = MessageBox.Choose(this, Txt.L($"„{name}“ läuft noch.\n\nDas Spiel kann im eigenen Fenster des Emulators weiterlaufen – oder es wird beendet; dann ist weg, was du im Spiel nicht gespeichert hast.",
                $"“{name}” is still running.\n\nThe game can keep running in the emulator’s own window – or it is quit; then whatever you haven’t saved in the game is gone."),
            Txt.L("MonHub schließen", "Close MonHub"), MessageBoxImage.Question,
            [Txt.L("Im eigenen Fenster weiterspielen", "Keep playing in its own window"), Txt.L("Spiel beenden", "Quit the game"), Txt.L("Abbrechen", "Cancel")]);
        if (choice == 0)
        {
            _closeDecided = true;
            Game.Release(toDesktop: true);
            Close();
        }
        else if (choice == 1)
        {
            _closeAfterGame = true;
            _restart = null;
            Game.QuitGame();
        }
        else Game.FocusGame();
    }

    /// <summary>"Beenden und neues starten": the running game closes, then the other one starts (inside MonHub).</summary>
    public void SwitchGame(string rom, string? emulator)
    {
        _restart = (rom, emulator);
        Game.QuitGame();
    }

    /// <summary>The emulator of the game running inside MonHub, or null.</summary>
    public string? GameEmulator => Game.Emulator;

    /// <summary>
    /// The DS or 3DS screen arrangement was changed while such a game runs: the emulators only read it when they start, so
    /// MonHub offers to start it again – after a save in the game (a restart loses whatever isn't saved).
    /// </summary>
    public void ArrangementChanged(bool threeDs)
    {
        if (Game.Rom is not { } rom || Game.Emulator is not { } emulator) return;
        if (threeDs ? emulator != GameLibrary.Azahar : emulator is not (GameLibrary.MelonDS or GameLibrary.DeSmuME)) return;
        if (MessageBox.Choose(this, Txt.L($"Die neue Anordnung zeigt {emulator} nach einem Neustart.\n\nHast du im Spiel gespeichert? Dann startet {emulator} jetzt neu – mit deinem Spielstand. Was nicht gespeichert ist, geht dabei verloren.",
                    $"{emulator} shows the new arrangement after a restart.\n\nHave you saved in the game? Then {emulator} restarts now – with your save. Whatever isn't saved is lost."),
                Txt.L("Anordnung", "Screen arrangement"), MessageBoxImage.Question,
                [Txt.L("Jetzt neu starten", "Restart now"), Txt.L("Nicht jetzt", "Not now")], enter: 1) != 0)
        {
            Game.FocusGame();
            return;
        }
        _restart = (rom, emulator);
        Game.QuitGame();
    }

    /// <summary>The game screen lies exactly over the pages – over the whole window in full screen.</summary>
    void PlaceGame()
    {
        if (!Game.IsVisible) return;
        Point at;
        double width, height;
        if (_fullScreen)
        {
            at = default;
            width = Root.Bounds.Width;
            height = Root.Bounds.Height;
        }
        else if (PageHost.TranslatePoint(new Point(0, 0), Root) is { } place)
        {
            at = place;
            width = PageHost.Bounds.Width;
            height = PageHost.Bounds.Height;
        }
        else return;
        if (Canvas.GetLeft(Game) != at.X) Canvas.SetLeft(Game, at.X);
        if (Canvas.GetTop(Game) != at.Y) Canvas.SetTop(Game, at.Y);
        if (Game.Width != width) Game.Width = width;
        if (Game.Height != height) Game.Height = height;
    }

    void SetMenuOpen(bool open)
    {
        _menuOpen = open;
        Rail.IsVisible = open && !_fullScreen;
        RailMini.IsVisible = !open && !_fullScreen;
        Game.SetMenuOpen(open);
    }

    void RailMini_Open(object? sender, RoutedEventArgs e) => SetMenuOpen(true);

    /// <summary>The folded menu: one icon per page, like the menu's own (built from it, so both always agree).</summary>
    void BuildMiniMenu()
    {
        foreach (var entry in Nav.Children.OfType<RadioButton>())
        {
            var page = (string)entry.Tag!;
            var icon = entry.GetLogicalDescendants().OfType<SpriteView>().First();
            var label = entry.GetLogicalDescendants().OfType<TextBlock>().First();
            var content = new Panel();
            content.Children.Add(new SpriteView { Key = icon.Key, Width = 32, Height = 32 });
            if (page == "start")
            {
                // the running game, like the dot in the open menu
                var dot = new Ellipse { Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -4, 0) };
                dot.Bind(Shape.FillProperty, dot.GetResourceObservable("Hub.Good"));
                content.Children.Add(dot);
            }
            var button = new Button { Content = content, Tag = page };
            button.Bind(ThemeProperty, button.GetResourceObservable("Hub.RailMini"));
            ToolTip.SetTip(button, label.Tag as string);
            if (page == "start") button.Classes.Add("current");
            button.Click += (_, _) =>
            {
                if (page == "start") Game.FocusGame();
                else Navigate(page); // another page: the menu opens again there, the game runs on
            };
            MiniNav.Children.Add(button);
        }
    }

    /// <summary>MonHub's own full screen: only the game, the bar comes when the mouse goes to the top edge.</summary>
    void SetFullScreen(bool on)
    {
        if (on == _fullScreen) return;
        _fullScreen = on;
        if (on)
        {
            _beforeFullScreen = WindowState;
            WindowState = WindowState.FullScreen;
        }
        else if (WindowState == WindowState.FullScreen)
            WindowState = _beforeFullScreen == WindowState.FullScreen ? WindowState.Normal : _beforeFullScreen;
        TitleBar.IsVisible = CaptionButtons.IsVisible = !on;
        SetMenuOpen(_menuOpen);
        Game.SetFullScreen(on);
        PlaceGame();
    }

    readonly List<System.IO.FileSystemWatcher> _watchers = new();
    DispatcherTimer? _diskChanged;

    /// <summary>
    /// ROMs, runs and saves are watched: a new save from the emulator, a ROM copied in the file manager, a new run – the
    /// page shows it 1½ s later (after the last change, so a save being written counts once), even while MonHub is in the back.
    /// </summary>
    void WatchFolders()
    {
        _diskChanged = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        // once more when the files have been quiet for a while: a save read while the emulator was still writing it
        // is not kept (see SaveInspector) – this second look shows the finished one
        var settled = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        void RefreshPage(DispatcherTimer timer)
        {
            timer.Stop();
            if (App.ShuttingDown || _closed || _current == null || WindowState == WindowState.Minimized || Blocked(this)) return; // a dialog: done when it closes
            HubSetup.EnsurePending();
            (Page(_current) as IHubPage)?.Refresh();
        }
        _diskChanged.Tick += (_, _) =>
        {
            RefreshPage(_diskChanged);
            settled.Stop();
            settled.Start();
        };
        settled.Tick += (_, _) => RefreshPage(settled);
        foreach (var dir in new[] { HubPaths.Roms, HubPaths.Randomized, HubPaths.Saves })
        {
            if (!System.IO.Directory.Exists(dir)) continue;
            var watcher = new System.IO.FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.DirectoryName | System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.Size,
                InternalBufferSize = 64 * 1024, // the maximum: Azahar writes many files at once
            };
            System.IO.FileSystemEventHandler changed = (_, _) => Dispatcher.UIThread.Post(() =>
            {
                _diskChanged.Stop();
                _diskChanged.Start();
            });
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += (s, e) => changed(s, e);
            // too many changes at once (a 3DS game writing its SD card) overflow the watcher's buffer: events were lost,
            // so read everything again
            watcher.Error += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                _diskChanged.Stop();
                _diskChanged.Start();
            });
            try
            {
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (System.IO.IOException)
            {
                watcher.Dispose(); // Linux: the system's limit of watched folders is used up – the page still refreshes on focus
            }
        }
    }

    /// <summary>Menu labels: retro themes write them in capitals, like the START menu ("DEX" keeps its small é).</summary>
    void ApplyThemeText()
    {
        bool caps = this.TryFindResource("Hub.NavCaps") is true;
        foreach (var label in Rail.GetLogicalDescendants().OfType<TextBlock>())
            if (label.Tag is string text) label.Text = caps ? text.ToUpperInvariant() : text;
    }

    /// <summary>Shows a page ("start", "games", "dex", "fangames", "emulators", "options"); "games" and "dex" can preselect a ROM.</summary>
    public void Navigate(string page, string? select = null, bool animate = true)
    {
        if (select != null && Page(page) is LibraryPage library) library.Select(select);
        if (select != null && Page(page) is DexPage dex) dex.Select(select);
        var entry = Nav.Children.OfType<RadioButton>().First(r => (string?)r.Tag == page);
        _quiet = !animate;
        try
        {
            if (entry.IsChecked == true) Show(page);
            else entry.IsChecked = true; // → Nav_Checked → Show
        }
        finally
        {
            _quiet = false;
        }
    }

    /// <summary>A page change without the fade-in (the tour measures the page right after it is shown).</summary>
    bool _quiet;

    void Nav_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string page }) Show(page);
    }

    void Show(string key)
    {
        var page = Page(key);
        bool changed = _current != key;
        _current = key;
        // while a game runs inside MonHub, "Weiter" is the game; every other page as always (the game runs on)
        bool game = key == "start" && Game.Running;
        foreach (var child in PageHost.Children)
            child.Visibility = child == page && !game ? Visibility.Visible : Visibility.Collapsed;
        Game.IsVisible = game;
        Game.SetShown(game);
        if (!game)
        {
            SetFullScreen(false);
            SetMenuOpen(true);
        }
        PlaceGame();
        (page as IHubPage)?.Refresh();
        if (changed && !_quiet) Enter(page);
    }

    Control Page(string key)
    {
        if (_pages.TryGetValue(key, out var page)) return page;
        page = key switch
        {
            "start" => new StartPage(),
            "games" => new LibraryPage(),
            "run" => new RandomizerPage(),
            "nuzlocke" => new NuzlockePage(),
            "dex" => new DexPage(),
            "fangames" => new FangamesPage(),
            "emulators" => new EmulatorsPage(),
            "options" => new OptionsPage(),
            _ => throw new ArgumentException($"Unknown page {key}", nameof(key)),
        };
        page.IsVisible = false;
        _pages[key] = page;
        PageHost.Children.Add(page);
        return page;
    }

    /// <summary>The new page fades in and moves up a few pixels (170 ms) – not when motion is off.</summary>
    void Enter(Control page)
    {
        if (HubMotion.Level == MotionLevel.Off || !IsVisible) return;
        var enter = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(170),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 10d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        };
        _ = enter.RunAsync(page);
    }

    /// <summary>
    /// After the start, the other pages are built, filled and measured one by one while MonHub is idle (kept hidden), so the
    /// first click on a menu entry shows a ready page instead of building it then – that first build was the stutter.
    /// </summary>
    void Prepare(Queue<string> pages)
    {
        if (_closed || App.ShuttingDown || pages.Count == 0) return;
        var key = pages.Dequeue();
        if (!_pages.ContainsKey(key))
        {
            var page = Page(key);
            page.Visibility = Visibility.Hidden; // takes part in layout, draws nothing
            (page as IHubPage)?.Refresh(); // its data is read now, not on the first click
            Dispatcher.UIThread.Post(() =>
            {
                if (_current != key && page.Visibility == Visibility.Hidden) page.Visibility = Visibility.Collapsed;
            }, DispatcherPriority.ApplicationIdle);
        }
        Dispatcher.UIThread.Post(() => Prepare(pages), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Back in MonHub (from the emulator, the file manager, a dialog): the page shows what changed meanwhile.</summary>
    void Window_Activated(object? sender, EventArgs e)
    {
        CaptionButtons.Opacity = 1;
        Game.FocusGame(); // back in MonHub while playing: the keys go to the game
        if (!_activatedOnce)
        {
            _activatedOnce = true; // just opened – the page was filled a moment ago
            Prepare(new Queue<string>(["games", "run", "emulators", "options", "nuzlocke", "dex", "fangames"]));
            // after a new install or an update: Porygon offers the tour (not on a normal restart)
            Dispatcher.UIThread.Post(() =>
            {
                // not over a running game – its window would cover the tour (the offer comes again next time)
                if (!_closed && !App.ShuttingDown && !Game.Running && Tour.Offer() is { } offer)
                    RunTour(offer.Mode, offer.Steps, ask: true);
            }, DispatcherPriority.ApplicationIdle);
            return;
        }
        if (App.ShuttingDown || _closed || _current == null) return;
        // "like the system": Windows' animation effects were switched on or off meanwhile
        if (HubConfig.Current.Motion == null && HubMotion.Level != HubConfig.Current.EffectiveMotion) HubMotion.Apply(HubConfig.Current.EffectiveMotion);
        HubSetup.EnsurePending(); // an emulator that was open is probably closed now
        (Page(_current) as IHubPage)?.Refresh();
    }

    /// <summary>Porygon's tour (Optionen → Tour): all stations, no question first.</summary>
    public void StartTour(TourMode mode)
    {
        // the game's window would lie over the pages the tour shows
        if (Game.Running)
        {
            MessageBox.Show(this, Txt.L("Während ein Spiel in MonHub läuft, liegt es über den Seiten, die Porygon zeigen will. Beende das Spiel oder schick es ins eigene Fenster – dann geht die Tour los.",
                    "While a game runs inside MonHub, it lies over the pages Porygon wants to show. Quit the game or send it to its own window – then the tour can start."),
                Txt.L("Tour", "Tour"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        RunTour(mode, Tour.For(mode), ask: false);
    }

    void RunTour(TourMode mode, List<TourStep> steps, bool ask)
    {
        var back = _current ?? "start";
        TourLayer.Start(mode, steps, ask,
            find: (page, name) => page == null ? this.FindName(name) : Page(page).FindName(name),
            navigate: page => Navigate(page, animate: false),
            done: () => Navigate(ask ? "start" : back));
    }

    void Help_Click(object? sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();

    // ---------------- title bar ----------------

    void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>The maximize button shows "restore" while the window is maximized.</summary>
    void Window_StateChanged()
    {
        bool max = WindowState == WindowState.Maximized;
        BtnMaximize.Tag = this.FindResource(max ? "Glyph.Restore" : "Glyph.Maximize");
        BtnMaximize.ToolTip = max ? Txt.L("Verkleinern", "Restore") : Txt.L("Maximieren", "Maximize");
    }

    /// <summary>Like the system's own title bar: dimmer while another window is active.</summary>
    void Window_Deactivated(object? sender, EventArgs e) => CaptionButtons.Opacity = 0.55;
}
