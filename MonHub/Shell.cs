using System.Diagnostics;
using System.IO;
using Avalonia.VisualTree;

namespace MonHub;

/// <summary>A page of the main window; refreshed whenever it is shown and when MonHub gets the focus back.</summary>
public interface IHubPage
{
    void Refresh();
}

/// <summary>Things every page needs: navigation, the tour, folders.</summary>
public static class Shell
{
    static MainWindow? Main => Compat.MainWindow as MainWindow;

    public static void Navigate(string page, string? select = null) => Main?.Navigate(page, select);

    /// <summary>Porygon's tour through MonHub (Optionen → "Tour").</summary>
    public static void StartTour() => Main?.StartTour(TourMode.Full);

    /// <summary>Shows a folder in the system's file manager (Explorer; on Linux whatever the desktop uses).</summary>
    public static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { dir }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(Main, Txt.L($"Der Ordner lässt sich nicht öffnen:\n{dir}\n\n{ex.Message}", $"The folder can't be opened:\n{dir}\n\n{ex.Message}"), "MonHub", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Opens a web page or a file with the program the system uses for it.</summary>
    public static void OpenWith(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { target }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(Main, ex.Message, "MonHub", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>A program's own icon in a small picture – hidden where there is none (Linux programs carry no icon).</summary>
    public static void ShowIcon(Image image, string? program)
    {
        image.Source = IconHelper.Get(program);
        image.IsVisible = image.Source != null;
    }

    /// <summary>A list inside a scrolling page hands the mouse wheel to the page.</summary>
    public static void ForwardWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled || sender is not Visual list) return;
        if (list.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is not { } page) return;
        e.Handled = true;
        page.Offset = page.Offset.WithY(Math.Max(0, page.Offset.Y - e.Delta.Y * 40));
    }
}
