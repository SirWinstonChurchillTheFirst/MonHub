using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace MonHub;

/// <summary>
/// A game running inside MonHub ("Weiter" while playing): the emulator's own window sits under a bar with the game's
/// name and Beenden / Im eigenen Fenster / Vollbild. The emulator stays the program it always was – MonHub only gives
/// its window a place. If the window can't be found (or taken), the game simply stays in its own window.
/// </summary>
public partial class GameScreen : UserControl
{
    static readonly Lazy<GameWindow?> NativeWindows = new(GameWindow.Create);

    /// <summary>Whether games can be shown inside MonHub on this system at all.</summary>
    public static bool Possible => NativeWindows.Value != null;

    static GameWindow Native => NativeWindows.Value!;

    readonly DispatcherTimer _tick;
    Process? _process;
    int _pid;
    IntPtr _game;
    DateTime _started;
    string? _emulator;
    double? _ratio;
    PixelPoint _onScreen;
    (int Left, int Top, int Right, int Bottom) _chrome;
    (int Left, int Top, int Right, int Bottom)? _chromeSeen;
    bool _shown, _fullScreen;
    // the loading screen: the emulator's place stays invisible until its window answers again and again
    bool _ready, _masked;
    DateTime _takenAt;
    int _answers;
    DateTime _barSeen;

    /// <summary>The emulator is gone (closed in the game, with Beenden, or it crashed).</summary>
    public event Action? Ended;

    /// <summary>The game left MonHub and runs on in its own window (asked for, or it couldn't be taken in).</summary>
    public event Action? Released;

    /// <summary>The menu button: show or hide MonHub's menu.</summary>
    public event Action? MenuClicked;

    /// <summary>The full screen button.</summary>
    public event Action? FullScreenClicked;

    /// <summary>Another screen arrangement was picked in the bar (already saved in the settings).</summary>
    public event Action? ArrangementChosen;

    /// <summary>The running game's emulator.</summary>
    public string? Emulator => _process != null ? _emulator : null;

    public GameScreen()
    {
        InitializeComponent();
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _tick.Tick += (_, _) => Tick();
        Host.SizeChanged += (_, _) => Fit();
        // while the window is being resized: right away, not only on the next timer tick
        Host.LayoutUpdated += (_, _) => Fit();
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window) window.PositionChanged += (_, _) => Fit();
        };
        SizeChanged += (_, _) => Fit();
        Host.Destroying += () => Release(toDesktop: true);
        // narrow: the buttons keep only their icons
        SizeChanged += (_, e) =>
        {
            foreach (var button in new[] { BtnScreens, BtnMenu, BtnFull, BtnOwn, BtnQuit }) button.Classes.Set("narrow", e.NewSize.Width < 900);
        };
        // filled before it opens (filled while opening, the menu came up empty)
        BtnScreens.Click += (_, _) =>
        {
            FillScreensMenu();
            _screensMenu.ShowAt(BtnScreens);
        };
        SetMenuOpen(true);
        SetFullScreen(false);
    }

    /// <summary>A game is in MonHub's hands (starting or running).</summary>
    public bool Running => _process != null;

    /// <summary>The ROM that is running.</summary>
    public string? Rom { get; private set; }

    /// <summary>
    /// Takes over a game that was just started: its window comes in as soon as it shows up. <paramref name="ratio"/>: the
    /// picture's width to height – the window gets just that room (null: all of it).
    /// </summary>
    public void Play(Process process, string rom, string? emulator, double? ratio)
    {
        _ratio = ratio;
        _chrome = default;
        _chromeSeen = null;
        _process = process;
        _pid = process.Id;
        _game = IntPtr.Zero;
        _started = DateTime.UtcNow;
        _emulator = emulator;
        Rom = rom;
        TxtTitle.Text = Path.GetFileNameWithoutExtension(rom);
        TxtEmulator.Text = emulator ?? "";
        _ready = false;
        _answers = 0;
        ShowLoading(rom, emulator);
        BtnScreens.IsVisible = emulator is GameLibrary.MelonDS or GameLibrary.DeSmuME or GameLibrary.Azahar;
        TxtNote.Text = Txt.L("startet …", "starting …");
        // the place has its full size from the start: an emulator squeezed to nothing while it sets up its picture
        // (Azahar, mGBA) can crash
        Host.IsVisible = _shown;
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.UIThread.Post(() => Gone(process));
            if (process.HasExited) Gone(process);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // no longer ours to watch: the timer notices when it is gone
        }
        _tick.Start();
    }

    /// <summary>The screen is on display (the "Weiter" page while playing) or not; the game runs on either way.</summary>
    public void SetShown(bool shown)
    {
        _shown = shown;
        Host.IsVisible = shown && _process != null;
        if (shown) Dispatcher.UIThread.Post(() => { Fit(); FocusGame(); }, DispatcherPriority.Background);
    }

    /// <summary>Keys to the game – only while MonHub is the active window (never pulls MonHub to the front).</summary>
    public void FocusGame()
    {
        if (_ready && _shown && _game != IntPtr.Zero && TopLevel.GetTopLevel(this) is Window { IsActive: true }) Native.Focus(_game);
    }

    public void SetMenuOpen(bool open)
    {
        TxtMenu.Text = open ? Txt.L("Menü aus", "Hide menu") : Txt.L("Menü", "Menu");
        ToolTip.SetTip(BtnMenu, open ? Txt.L("Menü ausblenden – mehr Platz fürs Spiel", "Hide the menu – more room for the game") : Txt.L("Menü zeigen", "Show the menu"));
    }

    /// <summary>Full screen: the bar only comes when the mouse goes to the top edge.</summary>
    public void SetFullScreen(bool on)
    {
        _fullScreen = on;
        TxtFull.Text = on ? Txt.L("Vollbild aus", "Exit full screen") : Txt.L("Vollbild", "Full screen");
        GlyphFull.Data = (Geometry)this.FindResource(on ? "Glyph.FullExit" : "Glyph.Full")!;
        ToolTip.SetTip(BtnFull, on ? Txt.L("Vollbild beenden – die Leiste kommt, wenn die Maus an den oberen Rand geht", "Exit full screen – the bar comes when the mouse goes to the top edge")
                                   : Txt.L("Vollbild – nur das Spiel", "Full screen – only the game"));
        BtnMenu.IsVisible = !on;
        // full screen: black around the picture, like a TV; inside the window the theme's own background
        ((Grid)Content!).Background = on ? Brushes.Black : Brushes.Transparent;
        Host.Margin = on ? default : new Thickness(0, 8, 0, 0);
        Bar.IsVisible = true;
        _barSeen = DateTime.UtcNow + (on ? TimeSpan.FromSeconds(2) : TimeSpan.Zero); // at first long enough to be seen
    }

    /// <summary>The game goes back to its own window and runs on there (MonHub closing, or the button).</summary>
    public void Release(bool toDesktop)
    {
        if (_process == null) return;
        if (toDesktop && _game != IntPtr.Zero && Host.Box != IntPtr.Zero && Native.Exists(_game)) Native.Release(_game, Host.Box);
        Stop();
        Released?.Invoke();
    }

    void Stop()
    {
        _tick.Stop();
        Ready(); // the place shows again (the next game hides it once more)
        _process = null;
        _game = IntPtr.Zero;
        Host.IsVisible = false;
        Rom = null;
    }

    void Gone(Process process)
    {
        if (_process != process) return;
        Stop();
        Ended?.Invoke();
    }

    void Tick()
    {
        if (_process == null) return;
        if (!Alive())
        {
            Gone(_process);
            return;
        }
        if (!_ready) WaitUntilReady();
        if (_game == IntPtr.Zero || !Native.Exists(_game))
        {
            // the first window – or a new one (some emulators build their window again, e.g. after a renderer change)
            if (Host.Box == IntPtr.Zero) return;
            var window = Native.Find(GameWindow.Family(_pid));
            // taken only into a place with a real size (see Play)
            if (window != IntPtr.Zero && Native.Size(Host.Box) is { Width: >= 64, Height: >= 64 })
            {
                _game = window;
                Native.Take(window, Host.Box);
                _takenAt = DateTime.UtcNow;
                Fit();
                FocusGame();
            }
            else if (_game == IntPtr.Zero && DateTime.UtcNow - _started > TimeSpan.FromSeconds(60))
            {
                Release(toDesktop: false); // no window to take: whatever comes up stays the emulator's own
            }
            return;
        }
        // the emulator's bars: taken only when two looks a moment apart agree – right after a resize the emulator may
        // not have laid out its inside yet, and a half-way value would make the place jump back and forth
        if (Native.Chrome(_game) is { } chrome)
        {
            if (_chromeSeen == chrome) _chrome = chrome;
            _chromeSeen = chrome;
        }
        Fit();
        // Linux: a click into the game doesn't give it the keys by itself (no Windows-style child focus) – MonHub does
        if (_shown && Native is X11GameWindow x11 && TopLevel.GetTopLevel(this) is Window { IsActive: true } && !x11.HasFocus(_game) && !MonHubControlFocused())
            Native.Focus(_game);
        if (_fullScreen) AutoHideBar();
    }

    /// <summary>A text field of MonHub's own has the keys (there are none in the bar – but a dialog might).</summary>
    bool MonHubControlFocused() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

    bool Alive()
    {
        try
        {
            return _process is { HasExited: false };
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            try
            {
                using var again = Process.GetProcessById(_pid);
                return !again.HasExited;
            }
            catch (Exception ex2) when (ex2 is ArgumentException or InvalidOperationException or Win32Exception)
            {
                return false;
            }
        }
    }

    /// <summary>In full screen the bar shows while the mouse is at the top edge (or on the bar), and goes 1 s after it left.</summary>
    void AutoHideBar()
    {
        if (Native.Cursor() is not { } cursor || TopLevel.GetTopLevel(this) is not { } top) return;
        var origin = this.PointToScreen(new Point(0, 0));
        double scale = top.RenderScaling;
        int barBottom = origin.Y + (int)((Bar.IsVisible ? Bar.Bounds.Height : 0) * scale);
        bool near = cursor.Y <= origin.Y + 3 || Bar.IsVisible && cursor.Y <= barBottom + 6;
        if (near) _barSeen = DateTime.UtcNow;
        bool show = near || DateTime.UtcNow - _barSeen < TimeSpan.FromSeconds(1);
        if (Bar.IsVisible != show)
        {
            Bar.IsVisible = show;
            Fit();
        }
    }

    /// <summary>The window fills the place (which is cut to the picture, see <see cref="SizeHost"/>).</summary>
    void Fit()
    {
        SizeHost();
        if (_game == IntPtr.Zero || Host.Box == IntPtr.Zero) return;
        // the size Avalonia is giving the place right now – the native place itself follows a moment later, and a game
        // sized after that one would lag behind every step of a window resize
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int w = (int)Math.Round(Host.Bounds.Width * scale), h = (int)Math.Round(Host.Bounds.Height * scale);
        if (w <= 0 || h <= 0) (w, h) = Native.Size(Host.Box);
        if (w > 0 && h > 0) Native.Place(_game, 0, 0, w, h);
        // the place moved on the screen (MonHub's window dragged, full screen): the game window stays where it is inside
        // it, so it isn't told – DeSmuME keeps drawing at the old spot on the screen. A nudge tells it.
        var onScreen = Host.PointToScreen(default);
        if (onScreen != _onScreen)
        {
            _onScreen = onScreen;
            Native.Moved(_game);
        }
    }

    /// <summary>The menu bar, toolbar and status bar of each emulator, in pixels at this scale (left, top, right, bottom).</summary>
    static (int, int, int, int) KnownBars(string? emulator, double scale)
    {
        var (top, bottom) = emulator switch
        {
            GameLibrary.MGBA => (Os.Windows ? 21 : 22, 0),
            GameLibrary.MelonDS => (Os.Windows ? 25 : 22, 0),
            GameLibrary.DeSmuME => (29, 0),
            GameLibrary.Azahar => (Os.Windows ? 33 : 22, Os.Windows ? 29 : 30),
            _ => (0, 0),
        };
        return (0, (int)Math.Round(top * scale), 0, (int)Math.Round(bottom * scale));
    }

    /// <summary>What MonHub measured about the running game (for tests): picture proportions and the emulator's bars.</summary>
    public string Measured => $"ratio {_ratio:0.###} chrome {_chrome} host {Host.Bounds.Size}";

    /// <summary>
    /// The place: as big as the area allows, but only as wide or high as the picture plus the emulator's own bars need –
    /// centred, the rest shows MonHub's background instead of black bars inside the emulator. (DeSmuME would also stretch
    /// its picture, and draw nothing at all away from the place's top left corner.)
    /// </summary>
    void SizeHost()
    {
        double width = double.NaN, height = double.NaN;
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        // the emulator's bars until they can be measured – or when they can't (an emulator drawing without the graphics
        // card has no picture window of its own to measure): as measured on Windows and Linux at 100 %
        var chrome = _chrome == default ? KnownBars(_emulator, scale) : _chrome;
        double w = Bounds.Width, h = Bounds.Height - (Bar.IsVisible ? Bar.Bounds.Height : 0) - Host.Margin.Top;
        double barsX = (chrome.Item1 + chrome.Item3) / scale, barsY = (chrome.Item2 + chrome.Item4) / scale;
        if (_ratio is double ratio && w > barsX + 32 && h > barsY + 32)
        {
            double pictureW = w - barsX, pictureH = h - barsY;
            if (pictureW / pictureH > ratio) pictureW = pictureH * ratio;
            else pictureH = pictureW / ratio;
            width = Math.Floor(pictureW + barsX);
            height = Math.Floor(pictureH + barsY);
        }
        // a pixel up or down isn't worth a new layout (and can't start a back and forth)
        if (!(Math.Abs(Host.Width - width) < 2) && !(double.IsNaN(Host.Width) && double.IsNaN(width))) Host.Width = width;
        if (!(Math.Abs(Host.Height - height) < 2) && !(double.IsNaN(Host.Height) && double.IsNaN(height))) Host.Height = height;
    }

    readonly MenuFlyout _screensMenu = new() { Placement = PlacementMode.BottomEdgeAlignedRight };

    /// <summary>The arrangements to pick from, the one set now ticked.</summary>
    void FillScreensMenu()
    {
        bool threeDs = _emulator == GameLibrary.Azahar;
        var now = threeDs ? HubConfig.Current.ThreeDsScreens : HubConfig.Current.DsScreens;
        _screensMenu.Items.Clear();
        foreach (var (value, de, en) in new[]
                 {
                     (ScreenArrangement.Auto, "Automatisch", "Automatic"), (ScreenArrangement.Stacked, "Untereinander", "Stacked"),
                     (ScreenArrangement.SideBySide, "Nebeneinander", "Side by side"), (ScreenArrangement.BigAndSmall, "Groß + klein", "Big + small"),
                 })
        {
            if (value == ScreenArrangement.BigAndSmall && _emulator == GameLibrary.DeSmuME) continue; // DeSmuME doesn't have it
            var item = new MenuItem { Header = (value == now ? "✓  " : "     ") + Txt.L(de, en) };
            item.Click += (_, _) =>
            {
                if (value == now) return;
                if (threeDs) HubConfig.Current.ThreeDsScreens = value;
                else HubConfig.Current.DsScreens = value;
                HubConfig.Current.Save();
                ArrangementChosen?.Invoke();
            };
            _screensMenu.Items.Add(item);
        }
    }

    void Menu_Click(object? sender, RoutedEventArgs e)
    {
        MenuClicked?.Invoke();
        Dispatcher.UIThread.Post(FocusGame, DispatcherPriority.Background);
    }

    void Full_Click(object? sender, RoutedEventArgs e)
    {
        FullScreenClicked?.Invoke();
        Dispatcher.UIThread.Post(FocusGame, DispatcherPriority.Background);
    }

    void Own_Click(object? sender, RoutedEventArgs e) => Release(toDesktop: true);

    void Quit_Click(object? sender, RoutedEventArgs e)
    {
        if (_process == null) return;
        // Enter: keep playing – nothing is lost by a key pressed by accident
        if (MessageBox.Choose(Compat.WindowOf(this), Txt.L("Spiel beenden? Was du im Spiel nicht gespeichert hast, ist danach weg.",
                "Quit the game? Whatever you haven't saved in the game is gone afterwards."),
                Txt.L("Beenden", "Quit"), MessageBoxImage.Question,
                [Txt.L("Spiel beenden", "Quit the game"), Txt.L("Weiterspielen", "Keep playing")], enter: 1) != 0)
        {
            Dispatcher.UIThread.Post(FocusGame, DispatcherPriority.Background);
            return;
        }
        QuitGame();
    }

    /// <summary>The loading screen: the game's Pokémon (its box art in MonHub), its name and the emulator.</summary>
    async void ShowLoading(string rom, string? emulator)
    {
        Loading.IsVisible = true;
        TxtLoading.Text = Txt.L($"„{Path.GetFileNameWithoutExtension(rom)}“ startet", $"“{Path.GetFileNameWithoutExtension(rom)}” is starting");
        TxtLoadingNote.Text = emulator != null ? Txt.L($"{emulator} lädt das Spiel …", $"{emulator} is loading the game …") : "";
        LoadingSprite.Key = "0025/idle";
        if (Host.Box != IntPtr.Zero && !_masked && MaskWhileLoading)
        {
            Native.Mask(Host.Box, true);
            _masked = true;
        }
        try
        {
            var games = await GameData.LoadAsync();
            if (games.FirstOrDefault(g => string.Equals(g.Rom, rom, StringComparison.OrdinalIgnoreCase)) is { } game && Rom == rom)
                LoadingSprite.Key = GameLook.For(game).Sprite;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Pikachu stays
        }
    }

    /// <summary>
    /// DeSmuME starts within a second – and never draws at all if its window was invisible when it set up its picture
    /// (DirectDraw), so it is shown right away.
    /// </summary>
    bool MaskWhileLoading => true;

    /// <summary>
    /// The game is shown once its window is in place and the emulator answered several times in a row (it is done
    /// loading) – after 30 s at the latest. Until then the dots of the loading screen move.
    /// </summary>
    void WaitUntilReady()
    {
        if (Host.Box != IntPtr.Zero && !_masked && MaskWhileLoading)
        {
            Native.Mask(Host.Box, true);
            _masked = true;
        }
        var dots = new string('.', 1 + (int)(DateTime.UtcNow.Ticks / TimeSpan.FromMilliseconds(400).Ticks % 3));
        TxtLoadingNote.Text = (_emulator != null ? Txt.L($"{_emulator} lädt das Spiel", $"{_emulator} is loading the game") : Txt.L("Lädt", "Loading")) + " " + dots;
        if (_game == IntPtr.Zero) return;
        _answers = Native.Responsive(_game) ? _answers + 1 : 0;
        var since = DateTime.UtcNow - _takenAt;
        // Azahar answers long before the game runs (its window stays black meanwhile) – it names the game in its title
        // ("Azahar 2126.1.2 | Pokémon Y") once it does
        bool gameRuns = _emulator != GameLibrary.Azahar || Native.Title(_game) is not { } title || title.Contains(" | ", StringComparison.Ordinal);
        if (_answers >= 5 && gameRuns && since > TimeSpan.FromSeconds(1.5) || since > TimeSpan.FromSeconds(30)) Ready();
    }

    void Ready()
    {
        if (!_ready) TxtNote.Text = ""; // "startet …"
        _ready = true;
        Loading.IsVisible = false;
        bool wasMasked = _masked;
        if (_game != IntPtr.Zero) Native.Settled(_game);
        if (_masked && Host.Box != IntPtr.Zero) Native.Mask(Host.Box, false);
        _masked = false;
        if (_process != null)
        {
            Fit();
            // shown again: DeSmuME only draws once it is told its window moved (as after a move on the screen)
            if (wasMasked && _game != IntPtr.Zero) Native.Moved(_game);
            FocusGame();
        }
    }

    /// <summary>Closes the game like the emulator's own close button would.</summary>
    public void QuitGame()
    {
        if (_process == null) return; // closed in the meantime
        if (_game != IntPtr.Zero && Native.Exists(_game))
        {
            Native.Close(_game);
            TxtNote.Text = Txt.L("wird beendet …", "closing …");
            // an emulator that hangs never closes by itself: after a while MonHub offers to end it
            var asked = _process;
            DispatcherTimer.RunOnce(() => OfferForceQuit(asked), TimeSpan.FromSeconds(8));
        }
        else Kill();
    }

    /// <summary>Still open long after "Beenden": end it the hard way? (Enter: keep waiting – it may just be saving.)</summary>
    void OfferForceQuit(Process asked)
    {
        if (_process != asked || !Alive()) return;
        var name = _emulator ?? Txt.L("Der Emulator", "The emulator");
        if (MessageBox.Choose(Compat.WindowOf(this), Txt.L($"{name} schließt sich nicht – vielleicht hängt er.\n\nHart beenden? Was im Spiel nicht gespeichert ist, geht dabei verloren.",
                    $"{name} doesn't close – maybe it hangs.\n\nEnd it the hard way? Whatever isn't saved in the game is lost."),
                Txt.L("Beenden", "Quit"), MessageBoxImage.Warning,
                [Txt.L("Hart beenden", "End it"), Txt.L("Weiter warten", "Keep waiting")], enter: 1) == 0)
            Kill();
        else if (_process == asked && Alive())
            DispatcherTimer.RunOnce(() => OfferForceQuit(asked), TimeSpan.FromSeconds(15));
    }

    void Kill()
    {
        try { _process?.Kill(); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
    }
}

/// <summary>MonHub's place for the emulator's window: a native window of its own inside MonHub's.</summary>
public class GameHost : NativeControlHost
{
    /// <summary>The native window (0 while there is none).</summary>
    public IntPtr Box { get; private set; }

    bool _static; // our own STATIC window (Windows), not Avalonia's

    /// <summary>Right before the box goes away – a game inside must leave first, or it would go down with it.</summary>
    public event Action? Destroying;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (OperatingSystem.IsWindows())
        {
            Box = CreateWindowEx(0, "STATIC", "", 0x40000000 | 0x10000000 | 0x02000000 | 0x4 /* WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | SS_BLACKRECT */,
                0, 0, 100, 100, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _static = Box != IntPtr.Zero;
            if (_static) return new PlatformHandle(Box, "HWND");
        }
        var handle = base.CreateNativeControlCore(parent);
        Box = handle.Handle;
        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Destroying?.Invoke();
        Box = IntPtr.Zero;
        if (_static)
        {
            _static = false;
            DestroyWindow(control.Handle);
        }
        else
            base.DestroyNativeControlCore(control);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr h);
}
