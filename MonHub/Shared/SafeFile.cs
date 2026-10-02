using System.IO;
using System.Text;

namespace RandoApp;

/// <summary>
/// Writing that never leaves a half-written file behind: the new content goes into a temporary file next to the target
/// first, which then replaces the target in one step. A crash, a full disk or pulling the plug in the middle keeps the
/// old settings/save instead of an empty or cut-off one.
/// </summary>
public static class SafeFile
{
    public static void WriteAllText(string path, string text, Encoding? encoding = null) =>
        Write(path, tmp => File.WriteAllText(tmp, text, encoding ?? new UTF8Encoding(false)));

    public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding? encoding = null) =>
        Write(path, tmp => File.WriteAllLines(tmp, lines, encoding ?? new UTF8Encoding(false)));

    public static void WriteAllBytes(string path, byte[] bytes) => Write(path, tmp => File.WriteAllBytes(tmp, bytes));

    static void Write(string path, Action<string> write)
    {
        var tmp = $"{path}.{Guid.NewGuid():N}.monhub-tmp"; // unique: a background save and one from the UI may meet
        try
        {
            write(tmp);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
                // a leftover temp file does no harm
            }
        }
    }
}
