using Avalonia.Animation;
using Avalonia.VisualTree;

namespace MonHub;

/// <summary>
/// Small looping theme animations (a blinking LED, the ▶ cursor, slowly turning C-Gear rings), set in XAML:
/// <c>&lt;local:Ambient.Animation&gt;&lt;Animation …/&gt;&lt;/local:Ambient.Animation&gt;</c>.
/// They only run while the element is on screen, its window is active and not blocked by a dialog, and motion is at the
/// full level – otherwise they are stopped (and start over when things are back).
/// </summary>
public static class Ambient
{
    public static readonly AttachedProperty<Animation?> AnimationProperty =
        AvaloniaProperty.RegisterAttached<Control, Animation?>("Animation", typeof(Ambient));

    public static Animation? GetAnimation(Control element) => element.GetValue(AnimationProperty);
    public static void SetAnimation(Control element, Animation? value) => element.SetValue(AnimationProperty, value);

    static Ambient()
    {
        AnimationProperty.Changed.AddClassHandler<Control>((element, e) =>
        {
            if (e.NewValue is Animation animation) _ = new Runner(element, animation);
        });
    }

    sealed class Runner
    {
        readonly Control _element;
        readonly Animation _animation;
        Window? _window;
        CancellationTokenSource? _running;
        DispatcherTimer? _watch;

        public Runner(Control element, Animation animation)
        {
            _element = element;
            _animation = animation;
            element.AttachedToVisualTree += (_, _) => Attach();
            element.DetachedFromVisualTree += (_, _) => Detach();
            if (element.IsAttachedToVisualTree()) Attach();
        }

        void Attach()
        {
            Detach();
            _window = Compat.WindowOf(_element);
            if (_window != null)
            {
                _window.Activated += OnChanged;
                _window.Deactivated += OnChanged;
                _window.PropertyChanged += OnWindowProperty;
            }
            HubWindow.BlockedChanged += Update;
            HubMotion.Changed += Update;
            Update();
        }

        void Detach()
        {
            if (_window != null)
            {
                _window.Activated -= OnChanged;
                _window.Deactivated -= OnChanged;
                _window.PropertyChanged -= OnWindowProperty;
                _window = null;
            }
            HubWindow.BlockedChanged -= Update;
            HubMotion.Changed -= Update;
            Stop();
            _watch?.Stop();
            _watch = null;
        }

        void OnChanged(object? sender, EventArgs e) => Update();

        void OnWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty) Update();
        }

        void Update()
        {
            bool allowed = HubMotion.Decorations && _window is { IsActive: true } && !HubWindow.Blocked(_window)
                           && _window.WindowState != WindowState.Minimized;
            if (!allowed)
            {
                Stop();
                _watch?.Stop();
                _watch = null;
                return;
            }
            // a hidden page keeps its decoration in the tree – look now and then whether it can be seen
            if (_watch == null)
            {
                _watch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
                _watch.Tick += (_, _) => Sync();
                _watch.Start();
            }
            Sync();
        }

        void Sync()
        {
            if (_element.IsEffectivelyVisible) Start();
            else Stop();
        }

        void Start()
        {
            if (_running != null) return;
            _running = new CancellationTokenSource();
            HubAnim.Loop(_animation, _element, _running.Token);
        }

        void Stop()
        {
            _running?.Cancel();
            _running = null;
        }
    }
}
