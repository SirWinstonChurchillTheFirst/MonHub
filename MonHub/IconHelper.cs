using System.Runtime.InteropServices;

namespace MonHub;

/// <summary>
/// Reads the (large) icon of an exe or file so tiles can show the real program icon. Windows only – on Linux a program
/// has no icon inside it, the tile then shows MonHub's own picture.
/// </summary>
public static class IconHelper
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr ExtractAssociatedIcon(IntPtr instance, System.Text.StringBuilder path, out ushort index);

    static readonly Dictionary<(string Path, int Size, DateTime Time), BitmapSource?> Cache = new();

    /// <summary>The program's icon – read once per file version (pages ask again every time they are shown).</summary>
    public static BitmapSource? Get(string? path, int size = 128)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
        var key = (path.ToLowerInvariant(), size, System.IO.File.GetLastWriteTimeUtc(path));
        lock (Cache)
            if (Cache.TryGetValue(key, out var known)) return known;
        var icon = Extract(path, size);
        lock (Cache)
            Cache[key] = icon;
        return icon;
    }

    static BitmapSource? Extract(string path, int size)
    {
        var icons = new IntPtr[1];
        var ids = new uint[1];
        IntPtr handle = IntPtr.Zero;
        try
        {
            if (PrivateExtractIcons(path, 0, size, size, icons, ids, 1, 0) > 0 && icons[0] != IntPtr.Zero)
                handle = icons[0];
            else
                handle = ExtractAssociatedIcon(IntPtr.Zero, new System.Text.StringBuilder(path, 260), out _);
            if (handle == IntPtr.Zero) return null;
            using var icon = System.Drawing.Icon.FromHandle(handle);
            using var picture = icon.ToBitmap();
            using var png = new MemoryStream();
            picture.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            png.Position = 0;
            return new Avalonia.Media.Imaging.Bitmap(png);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero) DestroyIcon(handle);
        }
    }
}
