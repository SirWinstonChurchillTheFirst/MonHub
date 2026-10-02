using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RandoApp;

/// <summary>
/// Theme sprites: front-facing animation rows and portraits (list in SpriteData.g.cs, built by tools/build_sprites.py).
/// Source: PMD Sprite Collab (sprites.pmdcollab.org) – original art from Pokémon Mystery Dungeon,
/// © Nintendo / Creatures / GAME FREAK / Spike Chunsoft / The Pokémon Company. Non-commercial fan use.
/// Keys look like "0282/idle", "0143/sleep" or "0197/portrait".
/// </summary>
public enum SpriteTint { None, GameBoy }

public static partial class Sprites
{
    /// <summary>Frame durations are in game frames (1/60 s), as in the sprite's AnimData.xml.</summary>
    record Anim(string File, int[] Durations);

    static readonly Dictionary<string, BitmapSource[]> Cache = new();

    /// <summary>More sprites from the host app (MonHub: every Pokémon, item icons): key → strip URI + frame durations.</summary>
    public static Func<string, (string Uri, int[] Durations)?>? MoreSprites { get; set; }

    /// <summary>How all sprites are coloured – the Game Boy theme shows them in its four greens.</summary>
    public static SpriteTint Tint { get; private set; }

    public static event Action? TintChanged;

    public static void SetTint(SpriteTint tint)
    {
        if (Tint == tint) return;
        Tint = tint;
        TintChanged?.Invoke();
    }

    static (string Uri, int[] Durations)? Source(string key) =>
        All.TryGetValue(key, out var anim) ? ($"pack://application:,,,/Assets/Sprites/{anim.File}", anim.Durations) : MoreSprites?.Invoke(key);

    public static BitmapSource[]? Frames(string? key)
    {
        if (key == null || Source(key) is not { } source) return null;
        var cacheKey = Tint == SpriteTint.None ? key : $"{Tint}|{key}";
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

        var strip = new BitmapImage(new Uri(source.Uri));
        int count = source.Durations.Length, w = strip.PixelWidth / count, h = strip.PixelHeight;
        var frames = Enumerable.Range(0, count).Select(i =>
        {
            BitmapSource frame = new CroppedBitmap(strip, new Int32Rect(i * w, 0, w, h));
            if (Tint == SpriteTint.GameBoy) frame = GameBoyColours(frame);
            frame.Freeze();
            return frame;
        }).ToArray();
        Cache[cacheKey] = frames;
        return frames;
    }

    public static BitmapSource? FirstFrame(string? key) => Frames(key)?[0];

    public static bool Has(string? key) => key != null && Source(key) != null;

    /// <summary>Frame durations of a sprite in game frames (1/60 s).</summary>
    internal static int[]? Durations(string? key) => key != null ? Source(key)?.Durations : null;

    /// <summary>The four shades of the original Game Boy screen, by brightness – done once per frame, then cached.</summary>
    static BitmapSource GameBoyColours(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);
        ReadOnlySpan<uint> shades = [0xFF0F380F, 0xFF306230, 0xFF8BAC0F, 0xFF9BBC0F]; // dark → light
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] < 128)
            {
                px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0;
                continue;
            }
            int light = (px[i + 2] * 299 + px[i + 1] * 587 + px[i] * 114) / 1000;
            uint c = shades[Math.Min(3, light / 64)];
            px[i] = (byte)c;
            px[i + 1] = (byte)(c >> 8);
            px[i + 2] = (byte)(c >> 16);
            px[i + 3] = 0xFF;
        }
        return BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
    }

    /// <summary>A crisp, self-animating image of the sprite (see <see cref="SpriteAnimator"/> for when it moves).</summary>
    public static Image CreateImage(string key, double height)
    {
        var image = new Image { Height = height, VerticalAlignment = VerticalAlignment.Bottom };
        SpriteAnimator.For(image).Key = key;
        return image;
    }
}

/// <summary>
/// When things may move. Off when Windows' "Animation effects" are off (the desktop version of prefers-reduced-motion)
/// or the player turned animations off in the app.
/// </summary>
public static class Motion
{
    static bool _enabled = SystemParameters.ClientAreaAnimation;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Changed?.Invoke();
        }
    }

    public static event Action? Changed;
}

/// <summary>
/// Plays a sprite animation on one Image – element-local, so a frame change only touches that image
/// (no application resource swap, which would walk the element tree of every open window).
/// It only runs while it can be seen and the window has the player's attention: loaded, visible, window not minimized,
/// not blocked by a dialog and active. Otherwise it rests on its current frame and costs nothing.
/// Timers run at background priority, so clicks and keys are always handled first.
/// </summary>
public sealed class SpriteAnimator
{
    static readonly DependencyProperty AnimatorProperty =
        DependencyProperty.RegisterAttached("Animator", typeof(SpriteAnimator), typeof(SpriteAnimator));

    readonly Image _image;
    Window? _window;
    BitmapSource[]? _frames;
    int[] _durations = [];
    int _index;
    DispatcherTimer? _timer;
    string? _key;

    SpriteAnimator(Image image)
    {
        _image = image;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        image.Loaded += (_, _) => Attach();
        image.Unloaded += (_, _) => Detach();
        image.IsVisibleChanged += (_, _) => Update();
    }

    /// <summary>The animator of an image (created on first use).</summary>
    public static SpriteAnimator For(Image image)
    {
        if (image.GetValue(AnimatorProperty) is SpriteAnimator existing) return existing;
        var animator = new SpriteAnimator(image);
        image.SetValue(AnimatorProperty, animator);
        return animator;
    }

    /// <summary>Sprite key ("0487/idle"); null clears the image.</summary>
    public string? Key
    {
        get => _key;
        set
        {
            if (_key == value) return;
            _key = value;
            _frames = Sprites.Frames(value);
            _durations = Sprites.Durations(value) ?? [];
            _index = 0;
            _image.Source = _frames?[0];
            Stop();
            Update();
        }
    }

    /// <summary>Only animate on demand (e.g. while the card is hovered or selected); otherwise the first frame.</summary>
    public bool Hold
    {
        get => _hold;
        set
        {
            if (_hold == value) return;
            _hold = value;
            if (value && _frames != null && _index != 0)
            {
                _index = 0;
                _image.Source = _frames[0];
            }
            Update();
        }
    }
    bool _hold;

    void Attach()
    {
        Detach();
        _window = Window.GetWindow(_image);
        if (_window != null)
        {
            _window.Activated += OnWindowChanged;
            _window.Deactivated += OnWindowChanged;
            _window.StateChanged += OnWindowChanged;
            _window.IsEnabledChanged += OnWindowEnabledChanged;
        }
        Motion.Changed += Update;
        Sprites.TintChanged += Recolour;
        Recolour();
        Update();
    }

    void Detach()
    {
        if (_window != null)
        {
            _window.Activated -= OnWindowChanged;
            _window.Deactivated -= OnWindowChanged;
            _window.StateChanged -= OnWindowChanged;
            _window.IsEnabledChanged -= OnWindowEnabledChanged;
            _window = null;
        }
        Motion.Changed -= Update;
        Sprites.TintChanged -= Recolour;
        Stop();
    }

    /// <summary>The theme's sprite colours changed (e.g. Game Boy greens): same frame, new colours.</summary>
    void Recolour()
    {
        if (_key == null) return;
        _frames = Sprites.Frames(_key);
        if (_frames != null) _image.Source = _frames[Math.Min(_index, _frames.Length - 1)];
    }

    void OnWindowChanged(object? sender, EventArgs e) => Update();
    void OnWindowEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => Update();

    bool ShouldRun =>
        Motion.Enabled && !_hold && _frames is { Length: > 1 } && _image.IsLoaded && _image.IsVisible
        && _window is { IsActive: true, IsEnabled: true } && _window.WindowState != WindowState.Minimized;

    void Update()
    {
        if (ShouldRun) Start();
        else Stop();
    }

    void Start()
    {
        if (_timer != null || _frames == null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Duration(_index) };
        _timer.Tick += (_, _) =>
        {
            if (_frames == null) return;
            _index = (_index + 1) % _frames.Length;
            _image.Source = _frames[_index];
            _timer!.Interval = Duration(_index);
        };
        _timer.Start();
    }

    void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    TimeSpan Duration(int index) => TimeSpan.FromSeconds((index < _durations.Length ? _durations[index] : 10) / 60.0);
}
