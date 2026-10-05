using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;

namespace MonHub;

/// <summary>
/// Small feedback animations: buttons give way when pressed, the die wobbles while something works and settles
/// when it's done, results slide in. Nothing moves with motion set to "aus".
/// </summary>
public static class HubAnim
{
    static bool On => HubMotion.Level != MotionLevel.Off;

    /// <summary>
    /// Plays an animation again and again until it is cancelled. (Avalonia only lets styles loop an animation forever;
    /// one started from code has to end – so it is one round at a time: forth, or forth and back.)
    /// </summary>
    public static async void Loop(Animation animation, Control element, CancellationToken stop)
    {
        if (animation.IterationCount.IsInfinite)
            animation.IterationCount = new IterationCount(animation.PlaybackDirection is PlaybackDirection.Alternate or PlaybackDirection.AlternateReverse ? 2UL : 1UL);
        try
        {
            while (!stop.IsCancellationRequested)
                await animation.RunAsync(element, stop);
        }
        catch (OperationCanceledException)
        {
            // stopped: the element is back on its own values
        }
    }

    // ---------------- buttons ----------------

    /// <summary>The scale a button was given for its press – only that one is animated (never someone else's transform).</summary>
    static readonly AttachedProperty<ScaleTransform?> PressScaleProperty =
        AvaloniaProperty.RegisterAttached<Button, ScaleTransform?>("PressScale", typeof(HubAnim));

    /// <summary>Every button in MonHub (and its dialogs) squeezes a little while pressed – one handler for all.</summary>
    public static void RegisterButtonPress()
    {
        InputElement.PointerPressedEvent.AddClassHandler<Button>((b, e) =>
        {
            if (e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) Press(b, true);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<Button>((b, _) => Press(b, false), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerExitedEvent.AddClassHandler<Button>((b, _) => Press(b, false), RoutingStrategies.Direct, handledEventsToo: true);
        InputElement.PointerCaptureLostEvent.AddClassHandler<Button>((b, _) => Press(b, false), RoutingStrategies.Direct, handledEventsToo: true);
    }

    static void Press(Button button, bool down)
    {
        // real buttons and chips only: no check boxes, scroll bar arrows or the arrow inside a drop-down
        if (button is CheckBox or RepeatButton || button.TemplatedParent is ComboBox or ScrollBar or Expander) return;
        var scale = button.GetValue(PressScaleProperty);
        if (scale == null)
        {
            if (!down || !On || !button.IsEnabled) return;
            if (button.RenderTransform != null) return; // someone else's transform
            scale = new ScaleTransform(1, 1)
            {
                Transitions =
                [
                    new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(70) },
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(70) },
                ],
            };
            button.SetValue(PressScaleProperty, scale);
            button.RenderTransformOrigin = RelativePoint.Center;
            button.RenderTransform = scale;
        }
        if (!ReferenceEquals(button.RenderTransform, scale)) return;
        double target = down && On ? (button.Bounds.Width > 260 ? 0.98 : 0.94) : 1; // wide buttons move less
        foreach (var transition in scale.Transitions!.OfType<DoubleTransition>())
        {
            transition.Duration = TimeSpan.FromMilliseconds(down ? 70 : 220);
            transition.Easing = down ? new QuadraticEaseOut() : new BackEaseOut();
        }
        scale.ScaleX = scale.ScaleY = target;
    }

    // ---------------- the die ----------------

    static readonly AttachedProperty<CancellationTokenSource?> WobbleProperty =
        AvaloniaProperty.RegisterAttached<Control, CancellationTokenSource?>("Wobble", typeof(HubAnim));

    /// <summary>The die rocks while work is going on.</summary>
    public static void Wobble(Control element, bool on)
    {
        element.GetValue(WobbleProperty)?.Cancel();
        element.SetValue(WobbleProperty, null);
        if (!on || !On) return;
        var wobble = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(320),
            IterationCount = new IterationCount(2),
            PlaybackDirection = PlaybackDirection.Alternate,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, -16d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 16d) } },
            },
        };
        var stop = new CancellationTokenSource();
        element.SetValue(WobbleProperty, stop);
        Loop(wobble, element, stop.Token);
    }

    /// <summary>Done: the ball clicks shut – a quick squash and bounce.</summary>
    public static void Click(Control element)
    {
        if (!On) return;
        var click = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(520),
            Children =
            {
                Scale(0, 1),
                Scale(0.21, 0.75),
                Scale(0.5, 1.18),
                Scale(1, 1),
            },
        };
        _ = click.RunAsync(element);

        static KeyFrame Scale(double cue, double value) => new()
        {
            Cue = new Cue(cue),
            Setters = { new Setter(ScaleTransform.ScaleXProperty, value), new Setter(ScaleTransform.ScaleYProperty, value) },
        };
    }

    // ---------------- text and panels ----------------

    /// <summary>Fades an element in while it slides up a few pixels (results, messages).</summary>
    public static void SlideIn(Control element, double distance = 10)
    {
        if (!On) return;
        var slide = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(260),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, distance) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        };
        _ = slide.RunAsync(element);
    }
}
