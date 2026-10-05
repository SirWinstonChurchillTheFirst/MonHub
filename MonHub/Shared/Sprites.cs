using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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

    /// <summary>Where MonHub's pictures live inside the program.</summary>
    public const string AssetRoot = "avares://MonHub/Assets/";

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
        All.TryGetValue(key, out var anim) ? ($"{AssetRoot}Sprites/{anim.File}", anim.Durations) : MoreSprites?.Invoke(key);

    public static BitmapSource[]? Frames(string? key)
    {
        if (key == null || Source(key) is not { } source) return null;
        var cacheKey = Tint == SpriteTint.None ? key : $"{Tint}|{key}";
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

        BitmapSource[] frames;
        try
        {
            using var stream = AssetLoader.Open(new Uri(source.Uri));
            using var strip = WriteableBitmap.Decode(stream);
            frames = Cut(strip, source.Durations.Length, Tint == SpriteTint.GameBoy);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or ArgumentException)
        {
            return null; // no such picture: the place stays empty
        }
        Cache[cacheKey] = frames;
        return frames;
    }

    /// <summary>The strip's frames as pictures of their own (a frame change then only swaps the image's source).</summary>
    static BitmapSource[] Cut(WriteableBitmap strip, int count, bool gameBoy)
    {
        using var all = strip.Lock();
        int w = all.Size.Width / count, h = all.Size.Height;
        var frames = new BitmapSource[count];
        var row = new byte[w * 4];
        // red and blue swap places between the two pixel layouts – the Game Boy shades need to know which is which
        bool bgra = all.Format == PixelFormat.Bgra8888;
        for (int i = 0; i < count; i++)
        {
            var frame = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), all.Format, strip.AlphaFormat);
            using (var target = frame.Lock())
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(all.Address + y * all.RowBytes + i * w * 4, row, 0, row.Length);
                    if (gameBoy) GameBoyColours(row, bgra);
                    Marshal.Copy(row, 0, target.Address + y * target.RowBytes, row.Length);
                }
            frames[i] = frame;
        }
        return frames;
    }

    public static BitmapSource? FirstFrame(string? key) => Frames(key)?[0];

    public static bool Has(string? key) => key != null && Source(key) != null;

    /// <summary>Frame durations of a sprite in game frames (1/60 s).</summary>
    internal static int[]? Durations(string? key) => key != null ? Source(key)?.Durations : null;

    /// <summary>The four shades of the original Game Boy screen, by brightness – done once per frame, then cached.</summary>
    static void GameBoyColours(byte[] px, bool bgra)
    {
        ReadOnlySpan<uint> shades = [0x0F380F, 0x306230, 0x8BAC0F, 0x9BBC0F]; // dark → light, as RRGGBB
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a < 128)
            {
                px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0;
                continue;
            }
            // stored colours may be multiplied by their alpha – undo that for the brightness
            int c0 = Math.Min(255, px[i] * 255 / a), g = Math.Min(255, px[i + 1] * 255 / a), c2 = Math.Min(255, px[i + 2] * 255 / a);
            int r = bgra ? c2 : c0, b = bgra ? c0 : c2;
            int light = (r * 299 + g * 587 + b * 114) / 1000;
            uint c = shades[Math.Min(3, light / 64)];
            byte sr = (byte)(c >> 16), sg = (byte)(c >> 8), sb = (byte)c;
            px[i] = bgra ? sb : sr;
            px[i + 1] = sg;
            px[i + 2] = bgra ? sr : sb;
            px[i + 3] = 0xFF;
        }
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
    static bool _enabled = Compat.SystemAnimations;

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
/// Plays a sprite animation on one Image – element-local, so a frame change only touches that image.
/// It only runs while it can be seen and the window has the player's attention: on screen, visible, window not minimized,
/// not blocked by a dialog and active. Otherwise it rests on its current frame and costs next to nothing.
/// Timers run at background priority, so clicks and keys are always handled first.
/// </summary>
public sealed class SpriteAnimator
{
    static readonly ConditionalWeakTable<Image, SpriteAnimator> Animators = new();

    /// <summary>How often a sprite that can't be seen right now (its page is hidden) looks whether it is back.</summary>
    static readonly TimeSpan Resting = TimeSpan.FromMilliseconds(400);

    readonly Image _image;
    Window? _window;
    BitmapSource[]? _frames;
    int[] _durations = [];
    int _index;
    DispatcherTimer? _timer;
    string? _key;
    bool _attached;

    SpriteAnimator(Image image)
    {
        _image = image;
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        image.AttachedToVisualTree += (_, _) => Attach();
        image.DetachedFromVisualTree += (_, _) => Detach();
        image.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty) Update();
        };
    }

    /// <summary>The animator of an image (created on first use).</summary>
    public static SpriteAnimator For(Image image) => Animators.GetValue(image, i => new SpriteAnimator(i));

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
        _attached = true;
        _window = Compat.WindowOf(_image);
        if (_window != null)
        {
            _window.Activated += OnWindowChanged;
            _window.Deactivated += OnWindowChanged;
            _window.PropertyChanged += OnWindowProperty;
        }
        HubWindow.BlockedChanged += Update;
        Motion.Changed += Update;
        Sprites.TintChanged += Recolour;
        Recolour();
        Update();
    }

    void Detach()
    {
        _attached = false;
        if (_window != null)
        {
            _window.Activated -= OnWindowChanged;
            _window.Deactivated -= OnWindowChanged;
            _window.PropertyChanged -= OnWindowProperty;
            _window = null;
        }
        HubWindow.BlockedChanged -= Update;
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

    void OnWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) Update();
    }

    bool ShouldRun =>
        Motion.Enabled && !_hold && _frames is { Length: > 1 } && _attached && _image.IsVisible
        && _window is { IsActive: true } && !HubWindow.Blocked(_window) && _window.WindowState != WindowState.Minimized;

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
            if (_frames == null || _timer == null) return;
            // a hidden page keeps its sprites in the tree: they rest until the page is back
            if (!_image.IsEffectivelyVisible)
            {
                _timer.Interval = Resting;
                return;
            }
            _index = (_index + 1) % _frames.Length;
            _image.Source = _frames[_index];
            _timer.Interval = Duration(_index);
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
