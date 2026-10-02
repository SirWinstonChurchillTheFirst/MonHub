using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MonHub;

/// <summary>Reads the (large) icon of an exe or file so tiles can show the real program icon.</summary>
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
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
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
            var bmp = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
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
