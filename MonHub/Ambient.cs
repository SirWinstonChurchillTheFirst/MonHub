using System.Windows;
using System.Windows.Media.Animation;

namespace MonHub;

/// <summary>
/// Small looping theme animations (a blinking LED, the ▶ cursor, slowly turning C-Gear rings), set in XAML:
/// <c>&lt;local:Ambient.Storyboard&gt;&lt;Storyboard …/&gt;&lt;/local:Ambient.Storyboard&gt;</c>.
/// They only run while the element is visible, its window is active and not blocked by a dialog, and motion is at the
/// full level – otherwise they are paused. A running WPF storyboard keeps the render loop going, so storyboards used
/// here should set Timeline.DesiredFrameRate low (a blink needs 4 frames per second, not 60).
/// </summary>
public static class Ambient
{
    public static readonly DependencyProperty StoryboardProperty = DependencyProperty.RegisterAttached(
        "Storyboard", typeof(Storyboard), typeof(Ambient), new PropertyMetadata(null, OnStoryboardChanged));

    public static Storyboard? GetStoryboard(DependencyObject d) => (Storyboard?)d.GetValue(StoryboardProperty);
    public static void SetStoryboard(DependencyObject d, Storyboard? value) => d.SetValue(StoryboardProperty, value);

    static void OnStoryboardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && e.NewValue is Storyboard storyboard)
            _ = new Runner(element, storyboard);
    }

    sealed class Runner
    {
        readonly FrameworkElement _element;
        readonly Storyboard _storyboard;
        Window? _window;
        bool _started, _running;

        public Runner(FrameworkElement element, Storyboard storyboard)
        {
            _element = element;
            _storyboard = storyboard;
            element.Loaded += (_, _) => Attach();
            element.Unloaded += (_, _) => Detach();
            element.IsVisibleChanged += (_, _) => Update();
            if (element.IsLoaded) Attach();
        }

        void Attach()
        {
            Detach();
            _window = Window.GetWindow(_element);
            if (_window != null)
            {
                _window.Activated += OnChanged;
                _window.Deactivated += OnChanged;
                _window.StateChanged += OnChanged;
                _window.IsEnabledChanged += OnEnabledChanged;
            }
            HubMotion.Changed += Update;
            Update();
        }

        void Detach()
        {
            if (_window != null)
            {
                _window.Activated -= OnChanged;
                _window.Deactivated -= OnChanged;
                _window.StateChanged -= OnChanged;
                _window.IsEnabledChanged -= OnEnabledChanged;
                _window = null;
            }
            HubMotion.Changed -= Update;
            if (_started)
            {
                _storyboard.Stop(_element);
                _storyboard.Remove(_element);
                _started = _running = false;
            }
        }

        void OnChanged(object? sender, EventArgs e) => Update();
        void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => Update();

        void Update()
        {
            bool run = HubMotion.Decorations && _element.IsLoaded && _element.IsVisible
                       && _window is { IsActive: true, IsEnabled: true } && _window.WindowState != WindowState.Minimized;
            if (run == _running) return;
            _running = run;
            if (run)
            {
                if (_started) _storyboard.Resume(_element);
                else
                {
                    _storyboard.Begin(_element, true);
                    _started = true;
                }
            }
            else if (_started)
                _storyboard.Pause(_element);
        }
    }
}
