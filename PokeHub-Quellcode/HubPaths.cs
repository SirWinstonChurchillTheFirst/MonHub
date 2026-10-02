using System.IO;

namespace PokeHub;

/// <summary>
/// Everything lives below one folder:
///   PokeHub\ROMs, Randomisierte ROMs, Spielstände, Fangames   – for the player
///   PokeHub\System\App, System\Emulatoren, System\Einstellungen – programs and settings
/// The launcher itself runs from System\App.
/// </summary>
public static class HubPaths
{
    public static string AppDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>The PokeHub folder: two levels above System\App (falls back to the exe folder while developing).</summary>
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
    public static string? MGBAExe => File.Exists(Path.Combine(MGBA, "mGBA.exe")) ? Path.Combine(MGBA, "mGBA.exe") : null;
    /// <summary>Azahar: 3DS (Gen 6–7). Each game has its own SD card folder below AzaharSaves.</summary>
    public static string Azahar => Path.Combine(Emulators, "Azahar");
    public static string AzaharSaves => Path.Combine(Saves, "Azahar");
    public static string? AzaharExe => File.Exists(Path.Combine(Azahar, "azahar.exe")) ? Path.Combine(Azahar, "azahar.exe") : null;
    /// <summary>Nuzlocke trackers, one file per ROM.</summary>
    public static string Nuzlocke => Path.Combine(SettingsDir, "Nuzlocke");
    /// <summary>The player's own DS BIOS/firmware dumps (never part of the setup).</summary>
    public static string Bios => Path.Combine(SystemDir, "BIOS");

    public static string PokeRandoConfig => Path.Combine(SettingsDir, "PokeRando.json");
    public static string HubConfig => Path.Combine(SettingsDir, "PokeHub.json");

    public static string? MelonDSExe => FirstExe(MelonDS, "melonDS*.exe");
    public static string? DeSmuMEExe => FirstExe(DeSmuME, "DeSmuME*.exe");

    static string? FirstExe(string folder, string pattern) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).OrderByDescending(f => f.Contains("x64")).FirstOrDefault() : null;

    public static readonly string[] RomExtensions = [".nds", ".gba", ".gbc", ".gb", ".3ds", ".cci", ".cxi", ".cia"];
}
