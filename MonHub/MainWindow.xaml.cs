using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MonHub;

/// <summary>
/// The shell: menu and page host. Pages are created on first visit and kept (switching back costs nothing), and the page
/// on screen is refreshed when it is shown and whenever MonHub gets the focus back – a save written by the emulator
/// in the meantime shows up right away.
/// </summary>
public partial class MainWindow : Window
{
    readonly Dictionary<string, FrameworkElement> _pages = new();
    string? _current;
    bool _closed, _activatedOnce;

    public MainWindow()
    {
        InitializeComponent();
        RandoApp.WindowFit.Apply(this);
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TxtVersion.Text = version != null ? $"v{version.Major}.{version.Minor}" : "";

        HubThemes.Changed += ApplyThemeText;
        Closed += (_, _) =>
        {
            _closed = true;
            HubThemes.Changed -= ApplyThemeText;
        };
        // the theme detail at the bottom of the menu only when there is room for it
        SizeChanged += (_, e) => RailDeco.Visibility = e.NewSize.Height < 720 ? Visibility.Collapsed : Visibility.Visible;
        ApplyThemeText();
        Navigate("start");
        WatchFolders();
        System.Windows.SystemParameters.StaticPropertyChanged += OnSystemSetting;
        Closed += (_, _) =>
        {
            System.Windows.SystemParameters.StaticPropertyChanged -= OnSystemSetting;
            foreach (var watcher in _watchers) watcher.Dispose();
        };
    }

    readonly List<System.IO.FileSystemWatcher> _watchers = new();
    System.Windows.Threading.DispatcherTimer? _diskChanged;

    /// <summary>
    /// ROMs, runs and saves are watched: a new save from the emulator, a ROM copied in the Explorer, a new run – the page
    /// shows it 1½ s later (after the last change, so a save being written counts once), even while MonHub is in the back.
    /// </summary>
    void WatchFolders()
    {
        _diskChanged = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        // once more when the files have been quiet for a while: a save read while the emulator was still writing it
        // is not kept (see SaveInspector) – this second look shows the finished one
        var settled = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        void RefreshPage(System.Windows.Threading.DispatcherTimer timer)
        {
            timer.Stop();
            if (App.ShuttingDown || _closed || _current == null || WindowState == WindowState.Minimized || !IsEnabled) return; // a dialog: done when it closes
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
            System.IO.FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(() =>
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
            watcher.Error += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                _diskChanged.Stop();
                _diskChanged.Start();
            });
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    /// <summary>"Wie Windows": turning Windows' animation effects on or off applies right away.</summary>
    void OnSystemSetting(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(System.Windows.SystemParameters.ClientAreaAnimation) && HubConfig.Current.Motion == null)
            Dispatcher.BeginInvoke(() => HubMotion.Apply(HubConfig.Current.EffectiveMotion));
    }

    /// <summary>Menu labels: retro themes write them in capitals, like the START menu ("DEX" keeps its small é).</summary>
    void ApplyThemeText()
    {
        bool caps = TryFindResource("Hub.NavCaps") is true;
        foreach (var label in Labels(Rail))
            label.Text = caps ? ((string)label.Tag).ToUpperInvariant() : (string)label.Tag;
    }

    static IEnumerable<TextBlock> Labels(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is TextBlock { Tag: string }) yield return (TextBlock)child;
            foreach (var label in Labels(child)) yield return label;
        }
    }

    /// <summary>Shows a page ("start", "games", "dex", "fangames", "emulators", "options"); "games" and "dex" can preselect a ROM.</summary>
    public void Navigate(string page, string? select = null, bool animate = true)
    {
        if (select != null && Page(page) is LibraryPage library) library.Select(select);
        if (select != null && Page(page) is DexPage dex) dex.Select(select);
        var entry = Nav.Children.OfType<RadioButton>().First(r => (string)r.Tag == page);
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

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string page }) Show(page);
    }

    void Show(string key)
    {
        var page = Page(key);
        bool changed = _current != key;
        _current = key;
        foreach (UIElement child in PageHost.Children)
            child.Visibility = child == page ? Visibility.Visible : Visibility.Collapsed;
        (page as IHubPage)?.Refresh();
        if (changed && !_quiet) Enter(page);
    }

    FrameworkElement Page(string key)
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
        page.Visibility = Visibility.Collapsed;
        _pages[key] = page;
        PageHost.Children.Add(page);
        return page;
    }

    /// <summary>
    /// The new page fades in and moves up a few pixels (170 ms) – not when motion is off. While it moves, the page is
    /// drawn once into a cached bitmap and only that picture is faded and moved: fading the live page re-renders its
    /// whole tree into a layer on every frame, which dropped frames on each menu change.
    /// </summary>
    void Enter(FrameworkElement page)
    {
        if (HubMotion.Level == MotionLevel.Off || !IsLoaded) return;
        var duration = TimeSpan.FromMilliseconds(170);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var shift = new TranslateTransform();
        page.RenderTransform = shift;
        page.CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(page).DpiScaleX) { SnapsToDevicePixels = true };
        // FillBehavior.Stop: afterwards the page is back on its plain values and no clock is left holding them
        var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => page.CacheMode = null; // sharp, live text again
        page.BeginAnimation(OpacityProperty, fade);
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
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
            Dispatcher.BeginInvoke(() =>
            {
                if (_current != key && page.Visibility == Visibility.Hidden) page.Visibility = Visibility.Collapsed;
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        Dispatcher.BeginInvoke(() => Prepare(pages), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Back in MonHub (from the emulator, Explorer, a dialog): the page shows what changed meanwhile.</summary>
    void Window_Activated(object? sender, EventArgs e)
    {
        if (!_activatedOnce)
        {
            _activatedOnce = true; // just opened – the page was filled a moment ago
            CaptionButtons.Opacity = 1;
            Prepare(new Queue<string>(["games", "run", "emulators", "options", "nuzlocke", "dex", "fangames"]));
            // after a new install or an update: Porygon offers the tour (not on a normal restart)
            Dispatcher.BeginInvoke(() =>
            {
                if (!_closed && !App.ShuttingDown && Tour.Offer() is { } offer)
                    RunTour(offer.Mode, offer.Steps, ask: true);
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }
        CaptionButtons.Opacity = 1;
        if (App.ShuttingDown || _closed || _current == null) return;
        HubSetup.EnsurePending(); // an emulator that was open is probably closed now
        (Page(_current) as IHubPage)?.Refresh();
    }

    /// <summary>Porygon's tour (Optionen → Tour): all stations, no question first.</summary>
    public void StartTour(TourMode mode) => RunTour(mode, Tour.For(mode), ask: false);

    void RunTour(TourMode mode, List<TourStep> steps, bool ask)
    {
        var back = _current ?? "start";
        TourLayer.Start(mode, steps, ask,
            find: (page, name) => (page == null ? FindName(name) : Page(page).FindName(name)) as FrameworkElement,
            navigate: page => Navigate(page, animate: false),
            done: () => Navigate(ask ? "start" : back));
    }

    void Help_Click(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();

    // ---------------- title bar ----------------

    void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    /// <summary>
    /// Maximized, Windows puts the frame outside the screen – with our own title bar that would cut off the edges, so
    /// the content moves in by the frame's size. The maximize button shows "restore".
    /// </summary>
    void Window_StateChanged(object? sender, EventArgs e)
    {
        bool max = WindowState == WindowState.Maximized;
        Root.Margin = max ? FrameThickness() : new Thickness(0);
        BtnMaximize.Content = max ? "\uE923" : "\uE922";
        BtnMaximize.ToolTip = max ? Txt.L("Verkleinern", "Restore") : Txt.L("Maximieren", "Maximize");
    }

    Thickness FrameThickness()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            uint dpiValue = (uint)Math.Round(96 * dpi.DpiScaleX);
            double frame = (NativeChrome.GetSystemMetricsForDpi(NativeChrome.SM_CXSIZEFRAME, dpiValue)
                            + NativeChrome.GetSystemMetricsForDpi(NativeChrome.SM_CXPADDEDBORDER, dpiValue)) / dpi.DpiScaleX;
            return frame > 0 ? new Thickness(frame) : new Thickness(8);
        }
        catch
        {
            return new Thickness(8); // Windows 10 before 1607: the usual size
        }
    }

    /// <summary>Like Windows' own title bar: dimmer while another window is active.</summary>
    void Window_Deactivated(object? sender, EventArgs e) => CaptionButtons.Opacity = 0.55;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Window_StateChanged(this, e); // may open maximized (last size)
        // after WindowChrome's own hook, so this one is asked first
        Dispatcher.BeginInvoke(() => (PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource)?.AddHook(ChromeHook),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Our maximize button is reported to Windows as the real one (HTMAXBUTTON) – that is what makes Windows 11 show its
    /// snap layouts when the pointer rests on it. Windows then sends the mouse to the frame, not to WPF, so hover and
    /// click are handled here.
    /// </summary>
    IntPtr ChromeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084, WM_NCMOUSELEAVE = 0x02A2, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2, HTMAXBUTTON = 9;
        switch (msg)
        {
            case WM_NCHITTEST:
                if (OverMaximize(lParam))
                {
                    if (BtnMaximize.Tag is not "pressed") BtnMaximize.Tag = "hover";
                    handled = true;
                    return HTMAXBUTTON;
                }
                BtnMaximize.Tag = null;
                break;
            case WM_NCMOUSELEAVE:
                BtnMaximize.Tag = null;
                break;
            case WM_NCLBUTTONDOWN when wParam.ToInt32() == HTMAXBUTTON:
                BtnMaximize.Tag = "pressed";
                handled = true;
                break;
            case WM_NCLBUTTONUP when wParam.ToInt32() == HTMAXBUTTON:
                BtnMaximize.Tag = null;
                handled = true;
                ToggleMaximize();
                break;
        }
        return IntPtr.Zero;
    }

    bool OverMaximize(IntPtr lParam)
    {
        if (!BtnMaximize.IsVisible) return false;
        int x = unchecked((short)(lParam.ToInt64() & 0xFFFF)), y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        try
        {
            var point = BtnMaximize.PointFromScreen(new Point(x, y));
            return point.X >= 0 && point.Y >= 0 && point.X < BtnMaximize.ActualWidth && point.Y < BtnMaximize.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false; // not on screen right now
        }
    }
}

static class NativeChrome
{
    public const int SM_CXSIZEFRAME = 32, SM_CXPADDEDBORDER = 92;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
