namespace MonHub;

/// <summary>
/// Loading the library off the UI thread: the pages switch at once and fill in when the data is there, so the window
/// never freezes while ROM headers and saves are read (slow drive, virus scanner on a 4 GB 3DS file …).
/// </summary>
public static class GameData
{
    static readonly object Gate = new();
    static Task<List<GameEntry>>? _running;

    /// <summary>
    /// All games with their save summaries already read (those are read lazily otherwise – on the UI thread).
    /// Pages asking while a load is running get that same load: after the start every page fills itself in the
    /// background, and they would otherwise scan the folders and read every ROM header side by side. The result is
    /// shared – callers only read it. A request after the load has finished reads the disk again.
    /// </summary>
    public static Task<List<GameEntry>> LoadAsync()
    {
        lock (Gate)
        {
            if (_running is { IsCompleted: false } running) return running;
            return _running = Task.Run(() =>
            {
                var games = GameLibrary.Load();
                foreach (var game in games) _ = game.Summary;
                return games;
            });
        }
    }

    /// <summary>
    /// What the pages show of the games: when it is unchanged, a page keeps its cards instead of building them again.
    /// Includes the current minute – "vor 3 Min." texts must move on.
    /// </summary>
    public static string Signature(IEnumerable<GameEntry> games) =>
        DateTime.Now.ToString("yyyyMMddHHmm") + "|" + string.Join("|", games.Select(g =>
            $"{g.Rom}*{g.RomTime.Ticks}*{g.NewestSave?.Path}*{g.NewestSave?.Time.Ticks}"));
}
