using System.IO;

namespace MonHub;

/// <summary>
/// Everything lives below one folder:
///   MonHub\ROMs, Randomisierte ROMs, Spielstände, Fangames   – for the player
///   MonHub\System\App, System\Emulatoren, System\Einstellungen – programs and settings
/// The launcher itself runs from System\App.
/// </summary>
public static class HubPaths
{
    public static string AppDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>The MonHub folder: two levels above System\App (falls back to the exe folder while developing).</summary>
    public static string Root
    {
        get
        {
            var app = new DirectoryInfo(AppDir);
            return app.Parent?.Name.Equals("System", StringComparison.OrdinalIgnoreCase) == true && app.Parent.Parent != null
                ? app.Parent.Parent.FullName
                : app.FullName;
        }
    }

    public static string SystemDir => Path.Combine(Root, "System");
    public static string Emulators => Path.Combine(SystemDir, "Emulatoren");
    public static string SettingsDir => Path.Combine(SystemDir, "Einstellungen");

    public static string Roms => Path.Combine(Root, "ROMs");
    public static string Randomized => Path.Combine(Root, "Randomisierte ROMs");
    public static string Saves => Path.Combine(Root, "Spielstände");
    public static string Fangames => Path.Combine(Root, "Fangames");

    public static string MelonDS => Path.Combine(Emulators, "melonDS");
    public static string DeSmuME => Path.Combine(Emulators, "DeSmuME");
    public static string MelonDSSaves => Path.Combine(Saves, "melonDS");
    public static string DeSmuMESaves => Path.Combine(Saves, "DeSmuME");
    /// <summary>Imported GBA/GB saves (MonHub has no GBA emulator – they're kept safe here).</summary>
    public static string GbaSaves => Path.Combine(Saves, "GBA und GB");
    /// <summary>mGBA: Game Boy, Game Boy Color and GBA (Gen 1–3).</summary>
    public static string MGBA => Path.Combine(Emulators, "mGBA");
    public static string MGBASaves => Path.Combine(Saves, "mGBA");
    public static string? MGBAExe => Os.FindProgram(MGBA, "mGBA");
    /// <summary>mGBA's config.ini and qt.ini: next to the exe on Windows (portable), in its own config folder on Linux.</summary>
    public static string MGBAConfigDir => Os.Windows ? MGBA : Path.Combine(MGBA, "config", "mgba");
    /// <summary>Azahar: 3DS (Gen 6–7). Each game has its own SD card folder below AzaharSaves.</summary>
    public static string Azahar => Path.Combine(Emulators, "Azahar");
    public static string AzaharSaves => Path.Combine(Saves, "Azahar");
    public static string? AzaharExe => Os.FindProgram(Azahar, "azahar");
    /// <summary>Nuzlocke trackers, one file per ROM.</summary>
    public static string Nuzlocke => Path.Combine(SettingsDir, "Nuzlocke");
    /// <summary>The player's own DS BIOS/firmware dumps (never part of the setup).</summary>
    public static string Bios => Path.Combine(SystemDir, "BIOS");

    public static string RandomizerConfig => Path.Combine(SettingsDir, "Randomizer.json");
    /// <summary>The randomizer itself (Universal Pokemon Randomizer ZX), shipped next to MonHub.exe.</summary>
    public static string RandomizerJar => Path.Combine(AppDir, "randomizer.jar");
    public static string HubConfig => Path.Combine(SettingsDir, "MonHub.json");

    public static string? MelonDSExe => Os.FindProgram(MelonDS, "melonDS");
    /// <summary>DeSmuME: Windows only (its Linux version is years behind and no longer built).</summary>
    public static string? DeSmuMEExe => Os.Windows ? Os.FindProgram(DeSmuME, "DeSmuME") : null;

    /// <summary>
    /// The folder with melonDS.toml for a melonDS in <paramref name="folder"/>: next to the exe on Windows; on Linux
    /// MonHub starts it with its settings in "config/melonDS" inside that folder.
    /// </summary>
    public static string MelonDSConfigDir(string folder) => Os.Windows ? folder : Path.Combine(folder, "config", "melonDS");

    public static string MelonDSToml => Path.Combine(MelonDSConfigDir(MelonDS), "melonDS.toml");

    public static readonly string[] RomExtensions = [".nds", ".gba", ".gbc", ".gb", ".3ds", ".cci", ".cxi", ".cia"];
}
