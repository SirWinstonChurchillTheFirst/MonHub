using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MonHub;

/// <summary>
/// Small feedback animations: buttons give way when pressed, the die wobbles while something works and settles
/// when it's done, results slide in. Nothing moves with motion set to "aus".
/// </summary>
public static class HubAnim
{
    static bool On => HubMotion.Level != MotionLevel.Off;

    // ---------------- buttons ----------------

    static readonly DependencyProperty PressScaleProperty =
        DependencyProperty.RegisterAttached("PressScale", typeof(ScaleTransform), typeof(HubAnim));

    /// <summary>Every button in MonHub (and its dialogs) squeezes a little while pressed – one handler for all.</summary>
    public static void RegisterButtonPress()
    {
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, _) => Press(s, true)), true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((s, _) => Press(s, false)), true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseLeaveEvent, new MouseEventHandler((s, _) => Press(s, false)), true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.LostMouseCaptureEvent, new MouseEventHandler((s, _) => Press(s, false)), true);
    }

    static void Press(object sender, bool down)
    {
        // real buttons and chips only: no check boxes, scroll bar arrows or the arrow inside a drop-down
        if (sender is not ButtonBase button || button is CheckBox or RepeatButton || button.TemplatedParent is ComboBox) return;
        var scale = button.GetValue(PressScaleProperty) as ScaleTransform;
        if (scale == null)
        {
            if (!down || !On || !button.IsEnabled) return;
            if (button.RenderTransform != Transform.Identity && button.RenderTransform != null) return; // someone else's transform
            scale = new ScaleTransform(1, 1);
            button.SetValue(PressScaleProperty, scale);
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            button.RenderTransform = scale;
        }
        if (!ReferenceEquals(button.RenderTransform, scale)) return;
        double target = down && On ? (button.ActualWidth > 260 ? 0.98 : 0.94) : 1; // wide buttons move less
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(down ? 70 : 220))
        {
            EasingFunction = down ? new QuadraticEase { EasingMode = EasingMode.EaseOut } : new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    // ---------------- the die ----------------

    /// <summary>The die rocks while work is going on.</summary>
    public static void Wobble(RotateTransform rotate, bool on)
    {
        if (!on || !On)
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            rotate.Angle = 0;
            return;
        }
        var wobble = new DoubleAnimation(-16, 16, TimeSpan.FromMilliseconds(320))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(wobble, 30);
        rotate.BeginAnimation(RotateTransform.AngleProperty, wobble);
    }

    /// <summary>Done: the ball clicks shut – a quick squash and bounce.</summary>
    public static void Click(ScaleTransform scale)
    {
        if (!On) return;
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.75, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520)),
            new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut }));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    // ---------------- text and panels ----------------

    /// <summary>Fades an element in while it slides up a few pixels (results, messages).</summary>
    public static void SlideIn(FrameworkElement element, double distance = 10)
    {
        if (!On) return;
        var move = new TranslateTransform(0, distance);
        if (element.RenderTransform == Transform.Identity || element.RenderTransform is TranslateTransform)
            element.RenderTransform = move;
        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(distance, 0, duration) { EasingFunction = ease });
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
    }
}
