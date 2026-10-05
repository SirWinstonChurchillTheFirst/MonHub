global using Avalonia;
global using Avalonia.Controls;
global using Avalonia.Controls.Primitives;
global using Avalonia.Input;
global using Avalonia.Interactivity;
global using Avalonia.Layout;
global using Avalonia.Media;
global using Avalonia.Threading;
global using MonHub;
// the names the pages were written with (MonHub started on WPF)
global using FrameworkElement = Avalonia.Controls.Control;
global using UIElement = Avalonia.Controls.Control;
global using DependencyObject = Avalonia.AvaloniaObject;
global using BitmapSource = Avalonia.Media.IImage;
global using Brush = Avalonia.Media.IBrush;

using Avalonia.VisualTree;

namespace MonHub;

/// <summary>Shown, invisible but taking its room, or gone. Avalonia only knows shown / gone – "Hidden" keeps the room by turning transparent.</summary>
public enum Visibility { Visible, Hidden, Collapsed }

public enum MessageBoxButton { OK, OKCancel, YesNo, YesNoCancel }
public enum MessageBoxImage { None, Information, Warning, Error, Question }
public enum MessageBoxResult { None, OK, Cancel, Yes, No }

/// <summary>
/// Small bridges so the pages read the same on Windows and Linux: visibility as one value, the size an element really
/// has, tooltips, "run this when the UI is idle".
/// </summary>
public static class Compat
{
    extension(Visual v)
    {
        public Visibility Visibility
        {
            get => !v.IsVisible ? Visibility.Collapsed : v.Opacity == 0 && v is InputElement { IsHitTestVisible: false } ? Visibility.Hidden : Visibility.Visible;
            set
            {
                // Hidden: stays in the layout, draws nothing and takes no clicks
                if (value == Visibility.Hidden)
                {
                    v.IsVisible = true;
                    v.Opacity = 0;
                    if (v is InputElement hidden) hidden.IsHitTestVisible = false;
                    return;
                }
                if (v.Opacity == 0 && v is InputElement { IsHitTestVisible: false } shown)
                {
                    v.Opacity = 1;
                    shown.IsHitTestVisible = true;
                }
                v.IsVisible = value == Visibility.Visible;
            }
        }

        public double ActualWidth => v.Bounds.Width;
        public double ActualHeight => v.Bounds.Height;
    }

    extension(Control c)
    {
        public object? ToolTip
        {
            get => Avalonia.Controls.ToolTip.GetTip(c);
            set => Avalonia.Controls.ToolTip.SetTip(c, value);
        }

        /// <summary>A named element of this page or window (null if there is none).</summary>
        public Control? FindName(string name) => c.FindControl<Control>(name);

        /// <summary>A theme value by its key (null if the theme has none).</summary>
        public object? TryFindResource(string key) => c.TryFindResource(key, c.ActualThemeVariant, out var value) ? value : null;
    }

    /// <summary>Keeps a property on a theme value: it follows when the theme changes.</summary>
    public static void SetResourceReference(this Control c, AvaloniaProperty property, string key) => c.Bind(property, c.GetResourceObservable(key));

    /// <summary>Adds text at the end (a log).</summary>
    public static void AppendText(this TextBox box, string text) => box.Text += text;

    /// <summary>Shows the end of the text (the newest log line).</summary>
    public static void ScrollToEnd(this TextBox box) => box.CaretIndex = box.Text?.Length ?? 0;

    /// <summary>The window an element sits in.</summary>
    public static Window? WindowOf(Visual? element) => element == null ? null : TopLevel.GetTopLevel(element) as Window;

    /// <summary>All elements below this one, depth first.</summary>
    public static IEnumerable<Visual> Descendants(Visual root) => root.GetVisualDescendants();

    /// <summary>The main window (null while starting or closing).</summary>
    public static Window? MainWindow => MainWindowOverride ??
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>Tests and screenshots have no desktop lifetime – they name their main window here.</summary>
    public static Window? MainWindowOverride { get; set; }

    /// <summary>System fonts for body text: Windows' own where it exists, the bundled Inter everywhere else.</summary>
    public const string UiFont = "Segoe UI Variable Text, Segoe UI, Inter, Noto Sans, DejaVu Sans";

    /// <summary>Whether the system asks for less motion (Windows: "Animation effects" off). Linux desktops have no common switch – on.</summary>
    public static bool SystemAnimations
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return true;
            try
            {
                return NativeMethods.SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out bool on, 0) ? on : true;
            }
            catch
            {
                return true;
            }
        }
    }

    static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint flags);
    }
}
