using System.Windows;

namespace RandoApp;

/// <summary>
/// Keeps windows inside the screen's work area. Small laptops (1366×768) or 125–150 % scaling
/// would otherwise push the title bar or the buttons off-screen. Shared with MonHub.
/// </summary>
public static class WindowFit
{
    const double Margin = 16;

    /// <summary>Call right after InitializeComponent (before the window is shown).</summary>
    public static void Apply(Window w)
    {
        var area = SystemParameters.WorkArea; // primary screen without the taskbar, in WPF units
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
