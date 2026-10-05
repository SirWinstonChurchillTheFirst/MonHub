
namespace RandoApp;

/// <summary>
/// Keeps windows inside the screen's work area. Small laptops (1366×768) or 125–150 % scaling
/// would otherwise push the title bar or the buttons off-screen. Shared with MonHub.
/// </summary>
public static class WindowFit
{
    const double Margin = 16;

    /// <summary>The primary screen without the taskbar, in the units windows are measured in (null: not known yet).</summary>
    public static Size? WorkArea(Window? w = null)
    {
        var screens = (w ?? Compat.MainWindow)?.Screens;
        if (screens?.Primary is not { } screen) return null;
        return new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling);
    }

    /// <summary>The primary screen: its work area (without the taskbar) and its full width, in logical pixels.</summary>
    public static (Rect WorkArea, int Width) PrimaryScreen()
    {
        try
        {
            var screens = (Compat.MainWindow ?? new Window()).Screens;
            if (screens.Primary is { } s)
                return (new Rect(s.WorkingArea.X / s.Scaling, s.WorkingArea.Y / s.Scaling, s.WorkingArea.Width / s.Scaling, s.WorkingArea.Height / s.Scaling),
                    (int)(s.Bounds.Width / s.Scaling));
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // no screen to ask (tests): a common size
        }
        return (new Rect(0, 0, 1920, 1040), 1920);
    }

    /// <summary>Call right after InitializeComponent (before the window is shown).</summary>
    public static void Apply(Window w)
    {
        if (WorkArea(w) is not { } area) return;
        double maxW = Math.Max(320, area.Width - Margin);
        double maxH = Math.Max(240, area.Height - Margin);

        w.MinWidth = Math.Min(w.MinWidth, maxW);
        w.MinHeight = Math.Min(w.MinHeight, maxH);
        if (w.Width > maxW) w.Width = maxW; // NaN (auto size) compares false
        if (w.Height > maxH) w.Height = maxH;
        if (w.SizeToContent != SizeToContent.Manual)
        {
            // auto-sized dialogs must not grow past the screen either (their content scrolls instead)
            w.MaxWidth = Math.Min(w.MaxWidth, maxW);
            w.MaxHeight = Math.Min(w.MaxHeight, maxH);
        }
    }
}
