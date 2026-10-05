using System.Reflection;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
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
        foreach (var child in PageHost.Children)
            child.Visibility = child == page ? Visibility.Visible : Visibility.Collapsed;
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
        if (!_activatedOnce)
        {
            _activatedOnce = true; // just opened – the page was filled a moment ago
            Prepare(new Queue<string>(["games", "run", "emulators", "options", "nuzlocke", "dex", "fangames"]));
            // after a new install or an update: Porygon offers the tour (not on a normal restart)
            Dispatcher.UIThread.Post(() =>
            {
                if (!_closed && !App.ShuttingDown && Tour.Offer() is { } offer)
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
    public void StartTour(TourMode mode) => RunTour(mode, Tour.For(mode), ask: false);

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
