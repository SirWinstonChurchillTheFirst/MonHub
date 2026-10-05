using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;

namespace MonHub;

/// <summary>
/// Porygon's tour over the real pages: a light shade over the app, a calm frame around the part he explains and a
/// card right next to it – below, above or beside, wherever it covers nothing – with a small tip pointing at it.
/// Keys: → / Enter next, ← back, Esc skip.
///
/// Performance rules (the first version stuttered): nothing animates on its own; a step change measures the target once
/// (plus up to three late looks while the page fills in the background) and sets positions directly; the only motion is
/// a short fade/slide of the light and the card (opacity and transform, no re-layout); resizing is debounced; all
/// handlers and timers go when the tour ends.
/// </summary>
public partial class TourOverlay : UserControl
{
    const double Gap = 14;   // between the highlighted part and the card
    const double Edge = 16;  // the card keeps this distance from the window edge
    const double Inflate = 6;

    readonly RectangleGeometry _full = new();
    readonly RectangleGeometry _hole = new() { RadiusX = 12, RadiusY = 12 };
    readonly DispatcherTimer _resize = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer _retry = new() { Interval = TimeSpan.FromMilliseconds(260) };
    int _retries;
    int _version; // a newer step makes pending callbacks of older ones do nothing (fast clicking)
    Rect? _shownHole;
    CancellationTokenSource? _fade;

    List<TourStep> _steps = [];
    TourMode _mode;
    bool _ask;
    int _index; // -1 = welcome, _steps.Count = goodbye
    Window? _window;

    Func<string?, string, Control?> _find = (_, _) => null;
    Action<string> _navigate = _ => { };
    Action _done = () => { };

    static bool Moving => HubMotion.Level != MotionLevel.Off;

    public TourOverlay()
    {
        InitializeComponent();
        Shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, _full, _hole);
        SizeChanged += (_, _) =>
        {
            if (!IsVisible) return;
            _resize.Stop(); // debounced: one placement after the window stopped changing
            _resize.Start();
        };
        _resize.Tick += (_, _) =>
        {
            _resize.Stop();
            Place(fade: false);
        };
        _retry.Tick += (_, _) =>
        {
            // the page fills in the background after navigating: look again a few times, move only if it moved
            if (++_retries >= 3) _retry.Stop();
            var hole = HoleFor(Target());
            if (hole is { } h && (_shownHole is not { } shown || Distance(h, shown) > 2)) Place(fade: false);
        };
    }

    /// <summary>Opens the tour. <paramref name="ask"/>: ask first whether to show it (first start after install/update).</summary>
    public void Start(TourMode mode, List<TourStep> steps, bool ask, Func<string?, string, Control?> find, Action<string> navigate, Action done)
    {
        _mode = mode;
        _steps = steps;
        _ask = ask;
        _find = find;
        _navigate = navigate;
        _done = done;

        IsVisible = true;
        _window?.RemoveHandler(KeyDownEvent, Overlay_KeyDown); // never twice
        _window = Compat.WindowOf(this);
        _window?.AddHandler(KeyDownEvent, Overlay_KeyDown, RoutingStrategies.Tunnel);
        Hide();
        // the first look once the overlay has its size (it was collapsed until now)
        int version = ++_version;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _version || !IsVisible) return;
            Show(-1);
            Focus();
        }, DispatcherPriority.Loaded);
    }

    // ---------------- steps ----------------

    void Show(int index)
    {
        _index = index;
        int version = ++_version;
        _retry.Stop();
        BtnBack.IsVisible = index > 0;
        BtnSecond.IsVisible = false;
        BtnSkip.IsVisible = true;
        BtnSkip.Content = Txt.L("Tour überspringen", "Skip tour");
        Track.IsVisible = false;
        Hide(); // the new text appears at its new place, not for a frame at the old one

        if (index < 0)
        {
            bool full = _mode == TourMode.Full;
            TxtStep.Text = full ? Txt.L("Kurze Tour · etwa eine Minute", "Short tour · about a minute") : Txt.L("Neu in dieser Version", "New in this version");
            Look(full ? "Happy" : "Surprised");
            TxtTitle.Text = full ? Txt.L("Hallo, ich bin Porygon!", "Hi, I'm Porygon!") : Txt.L("MonHub hat ein Update!", "MonHub got an update!");
            TxtBody.Text = full
                ? Txt.L("Soll ich dir zeigen, wo was ist?", "Shall I show you where everything is?")
                : _steps.Count == 1
                    ? Txt.L("Soll ich dir das Neue zeigen? Nur ein Schritt.", "Shall I show you what's new? Just one step.")
                    : Txt.L($"Soll ich dir das Neue zeigen? {_steps.Count} kurze Schritte.", $"Shall I show you what's new? {_steps.Count} short steps.");
            BtnNext.Content = Txt.L("Tour starten", "Start tour");
            if (_ask)
            {
                BtnSkip.Content = Txt.L("Nein, danke", "No, thanks");
                BtnSecond.Content = Txt.L("Später", "Later");
                BtnSecond.IsVisible = true;
            }
            else
                BtnSkip.Content = Txt.L("Abbrechen", "Cancel");
            Place(fade: true);
            return;
        }
        if (index >= _steps.Count)
        {
            TxtStep.Text = Txt.L("Fertig", "Done");
            Look("Joyous");
            TxtTitle.Text = Txt.L("Das war's!", "That's it!");
            TxtBody.Text = Txt.L("Viel Spaß beim Spielen – die Tour findest du jederzeit unter Optionen.", "Have fun playing – you'll find the tour under Options any time.");
            BtnNext.Content = Txt.L("Los geht's", "Let's go");
            BtnSkip.IsVisible = false;
            Place(fade: true);
            return;
        }

        var step = _steps[index];
        TxtStep.Text = Txt.L($"Schritt {index + 1} von {_steps.Count}", $"Step {index + 1} of {_steps.Count}");
        Look(index % 3 == 2 ? "Inspired" : "Normal");
        TxtTitle.Text = step.Title;
        TxtBody.Text = step.Text;
        BtnNext.Content = index == _steps.Count - 1 ? Txt.L("Fertig", "Done") : Txt.L("Weiter →", "Next →");
        Track.IsVisible = true;
        if (step.Page != null) _navigate(step.Page);
        // measure once the page is shown (and scrolled to the part), not while it is still being built
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _version || !IsVisible) return;
            Target()?.BringIntoView();
            Dispatcher.UIThread.Post(() =>
            {
                if (version != _version || !IsVisible) return;
                Place(fade: true);
                _retries = 0;
                _retry.Start();
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded); // Loaded runs after the layout pass, ahead of background work
    }

    /// <summary>The card waits invisibly (still clickable – fast clicking goes on) until it is placed.</summary>
    void Hide()
    {
        StopFade();
        Card.Opacity = 0;
    }

    void StopFade()
    {
        _fade?.Cancel();
        _fade = null;
    }

    /// <summary>Porygon's face for what it says (PMD portraits; the sprite code tints them for the Game Boy look).</summary>
    void Look(string emotion) => Face.Key = $"portrait/0137/{emotion}";

    void Next_Click(object? sender, RoutedEventArgs e)
    {
        if (_index >= _steps.Count) End(seen: true);
        else Show(_index + 1);
    }

    void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (_index > 0) Show(_index - 1);
    }

    /// <summary>"Später" on the question: asked again on the next start.</summary>
    void Second_Click(object? sender, RoutedEventArgs e) => End(seen: false);

    void Skip_Click(object? sender, RoutedEventArgs e) => End(seen: true);

    void End(bool seen)
    {
        _version++;
        _retry.Stop();
        _resize.Stop();
        _window?.RemoveHandler(KeyDownEvent, Overlay_KeyDown);
        _window = null;
        StopFade();
        Light.Opacity = 1;
        IsVisible = false;
        _shownHole = null;
        if (seen) Tour.MarkSeen();
        else Tour.AskLater();
        _done();
    }

    void Overlay_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsVisible) return;
        switch (e.Key)
        {
            case Key.Right or Key.Enter:
                Next_Click(this, e);
                e.Handled = true;
                break;
            case Key.Left:
                Back_Click(this, e);
                e.Handled = true;
                break;
            case Key.Escape:
                End(seen: !(_ask && _index < 0)); // Esc on the question = "später"
                e.Handled = true;
                break;
            case Key.Tab or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Space:
                e.Handled = true; // the page below stays as it is shown
                break;
        }
    }

    // ---------------- placing ----------------

    Control? Target()
    {
        if (_index < 0 || _index >= _steps.Count || _steps[_index].Target is not { } names) return null;
        var page = _steps[_index].Page;
        foreach (var name in names.Split('|'))
            if (_find(page, name) is { IsEffectivelyVisible: true } element && element.Bounds is { Width: > 0, Height: > 0 })
                return element;
        return null;
    }

    Rect? HoleFor(Control? target)
    {
        if (target == null || Bounds.Width <= 0) return null;
        if (target.TranslatePoint(default, this) is not { } corner) return null; // not in this window's tree (yet)
        var hole = new Rect(corner, target.Bounds.Size).Inflate(Inflate)
            .Intersect(new Rect(4, 4, Math.Max(0, Bounds.Width - 8), Math.Max(0, Bounds.Height - 8)));
        return hole.Width < 8 || hole.Height < 8 ? null : hole;
    }

    static double Distance(Rect a, Rect b) =>
        Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), Math.Max(Math.Abs(a.Width - b.Width), Math.Abs(a.Height - b.Height)));

    enum Side { None, Below, Above, Right, Left }

    /// <summary>Shade, frame and card for the current step – positions are set directly, only the fade-in moves.</summary>
    void Place(bool fade)
    {
        if (!IsVisible || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            if (fade) Card.Opacity = 1; // never left invisible
            return;
        }
        var area = new Rect(0, 0, Bounds.Width, Bounds.Height);
        _full.Rect = area;

        var light = HoleFor(Target());
        bool lightChanged = light is { } l ? _shownHole is not { } shown || Distance(l, shown) > 2 : _shownHole != null;
        if (light is { } hole)
        {
            _hole.Rect = hole;
            foreach (var (frame, grow) in new[] { (FrameSoft, 3.0), (FrameLine, 0.0) })
            {
                Canvas.SetLeft(frame, hole.X - grow);
                Canvas.SetTop(frame, hole.Y - grow);
                frame.Width = hole.Width + 2 * grow;
                frame.Height = hole.Height + 2 * grow;
                frame.IsVisible = true;
            }
        }
        else
        {
            _hole.Rect = default;
            FrameSoft.IsVisible = FrameLine.IsVisible = false;
        }
        _shownHole = light;

        // the card: measured on its own (no layout pass of the window), then put where it covers nothing
        Card.Width = Math.Min(380, area.Width - 2 * Edge);
        TrackFill.Width = Math.Max(0, (Card.Width - 36) * (_index + 1) / Math.Max(1, _steps.Count));
        Card.Measure(new Size(Card.Width, double.PositiveInfinity));
        var size = new Size(Card.Width, Card.DesiredSize.Height);
        var (spot, side) = light is { } target ? Spot(target, size, area) : (Centered(size, area), Side.None);
        Canvas.SetLeft(Card, spot.X);
        Canvas.SetTop(Card, spot.Y);
        PlaceTip(side, light, spot, size);

        if (!fade) return;
        StopFade();
        Card.Opacity = 1;
        if (!Moving) return;
        var (dx, dy) = side switch
        {
            Side.Below => (0.0, 8.0),
            Side.Above => (0.0, -8.0),
            Side.Right => (8.0, 0.0),
            Side.Left => (-8.0, 0.0),
            _ => (0.0, 8.0),
        };
        _fade = new CancellationTokenSource();
        var time = TimeSpan.FromMilliseconds(200);
        if (lightChanged)
            _ = new Animation
            {
                Duration = time,
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.35) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
                },
            }.RunAsync(Light, _fade.Token);
        _ = new Animation
        {
            Duration = time,
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.XProperty, dx), new Setter(TranslateTransform.YProperty, dy) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, 0d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        }.RunAsync(Card, _fade.Token);
    }

    static Point Centered(Size card, Rect area) =>
        new(Math.Max(Edge, (area.Width - card.Width) / 2), Math.Max(Edge, (area.Height - card.Height) / 2));

    /// <summary>
    /// Below the part if there is room, else above, right, left. If nothing fits (a part as big as the page), the place
    /// that covers the least of it.
    /// </summary>
    static (Point Spot, Side Side) Spot(Rect hole, Size card, Rect area)
    {
        const double tip = 8;
        double cx = hole.X + hole.Width / 2, cy = hole.Y + hole.Height / 2;
        double ClampX(double x) => Math.Clamp(x, Edge, Math.Max(Edge, area.Width - Edge - card.Width));
        double ClampY(double y) => Math.Clamp(y, Edge, Math.Max(Edge, area.Height - Edge - card.Height));
        var options = new (Side Side, Point Spot, bool Fits)[]
        {
            (Side.Below, new Point(ClampX(cx - card.Width / 2), hole.Bottom + Gap + tip), hole.Bottom + Gap + tip + card.Height <= area.Height - Edge),
            (Side.Above, new Point(ClampX(cx - card.Width / 2), hole.Top - Gap - tip - card.Height), hole.Top - Gap - tip - card.Height >= Edge),
            (Side.Right, new Point(hole.Right + Gap + tip, ClampY(cy - card.Height / 2)), hole.Right + Gap + tip + card.Width <= area.Width - Edge),
            (Side.Left, new Point(hole.Left - Gap - tip - card.Width, ClampY(cy - card.Height / 2)), hole.Left - Gap - tip - card.Width >= Edge),
        };
        foreach (var option in options)
            if (option.Fits) return (option.Spot, option.Side);

        // nothing free: inside the window, covering as little of the part as possible (no tip – it would point into it)
        (Point Spot, double Cover) best = (default, double.MaxValue);
        foreach (var option in options)
        {
            var spot = new Point(ClampX(option.Spot.X), ClampY(option.Spot.Y));
            var box = new Rect(spot, card).Intersect(hole);
            double cover = box.Width <= 0 || box.Height <= 0 ? 0 : box.Width * box.Height;
            if (cover < best.Cover) best = (spot, cover);
        }
        return (best.Spot, Side.None);
    }

    /// <summary>The small tip on the card's edge, pointing at the middle of the part (kept off the rounded corners).</summary>
    void PlaceTip(Side side, Rect? light, Point spot, Size card)
    {
        if (side == Side.None || light is not { } hole)
        {
            Tip.IsVisible = false;
            return;
        }
        const double half = 7;
        double along = side is Side.Below or Side.Above
            ? Math.Clamp(hole.X + hole.Width / 2 - spot.X - half, 22, card.Width - 36)
            : Math.Clamp(hole.Y + hole.Height / 2 - spot.Y - half, 18, card.Height - 32);
        (Tip.HorizontalAlignment, Tip.VerticalAlignment, Tip.Margin) = side switch
        {
            Side.Below => (HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(along, -half, 0, 0)),
            Side.Above => (HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(along, 0, 0, -half)),
            Side.Right => (HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(-half, along, 0, 0)),
            _ => (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, along, -half, 0)),
        };
        Tip.IsVisible = true;
    }
}
