using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MonHub;

/// <summary>A page of the main window; refreshed whenever it is shown and when MonHub gets the focus back.</summary>
public interface IHubPage
{
    void Refresh();
}

/// <summary>Things every page needs: navigation, the tour, folders.</summary>
public static class Shell
{
    static MainWindow? Main => Application.Current?.MainWindow as MainWindow;

    public static void Navigate(string page, string? select = null) => Main?.Navigate(page, select);

    /// <summary>Porygon's tour through MonHub (Optionen → "Tour").</summary>
    public static void StartTour() => Main?.StartTour(TourMode.Full);

    public static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(Main!, Txt.L($"Der Ordner lässt sich nicht öffnen:\n{dir}\n\n{ex.Message}", $"The folder can't be opened:\n{dir}\n\n{ex.Message}"), "MonHub", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>A list inside a scrolling page hands the mouse wheel to the page.</summary>
    public static void ForwardWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject list) return;
        for (var d = VisualTreeHelper.GetParent(list); d != null; d = VisualTreeHelper.GetParent(d))
            if (d is ScrollViewer page)
            {
                e.Handled = true;
                page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta / 3.0);
                return;
            }
    }
}
