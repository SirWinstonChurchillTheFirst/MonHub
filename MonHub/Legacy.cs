using System.IO;

namespace MonHub;

/// <summary>
/// The names MonHub used before 2.7 (when it still had its old name) – only here, only to carry an existing install over:
/// settings files get their new names on the first start, and an old theme id is read as the new one (HubConfig.Load).
/// </summary>
public static class Legacy
{
    /// <summary>The red theme's id up to 2.6.</summary>
    public const string RedThemeId = "pokedex";

    static readonly (string Old, string New)[] SettingsFiles =
    [
        ("PokeHub.json", Path.GetFileName(HubPaths.HubConfig)),
        ("PokeRando.json", Path.GetFileName(HubPaths.RandomizerConfig)),
        ("PokeHub-crash.log", "MonHub-crash.log"),
    ];

    /// <summary>Renames the settings files of an older install (once; a file under the new name is never replaced).</summary>
    public static void MoveSettings()
    {
        foreach (var (old, now) in SettingsFiles)
        {
            try
            {
                var from = Path.Combine(HubPaths.SettingsDir, old);
                var to = Path.Combine(HubPaths.SettingsDir, now);
                if (File.Exists(from) && !File.Exists(to)) File.Move(from, to);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // locked or read-only: MonHub starts with fresh settings, the old file stays where it is
            }
        }
    }
}
