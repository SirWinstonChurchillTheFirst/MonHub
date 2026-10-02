using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RandoApp;

/// <summary>Built-in Action Replay codes: 999 Rare Candies (item 0x32, qty 0x3E7) into the bag.</summary>
public static class RareCandyCodes
{
    /// <param name="RomVersion">Only for game codes shared by several regions: the NDS header version the code is for.</param>
    public record Code(string Game, string Codes, string Activation, bool Tested, byte? RomVersion = null);

    // Each code writes exactly ONE bag slot (Medicine pocket, slot 1). No E-type block writes: those overwrite the
    // whole pocket and, if the data length is off, spill the following code words into it as garbage items.
    //
    // All codes verified in an emulator (py-desmume, see tools/cheatlab): booted each ROM with a save that has marker
    // items (Potion x123, Super Potion x45, Hyper Potion x89) in the Medicine pocket, continued into the game and
    // checked that the code's pointer chain lands exactly on the first marker in RAM. Black/White 1 (no save
    // available): new game, trainer name found exactly at the code's struct base + save offset 0x19404.
    // Platin (DE) and Schwarz 2 (DE) were additionally confirmed by actually playing.
    const string DP = "94000130 FCFF0000\nB21C4D28 00000000\nB0000004 00000000\n00000DAC 03E70032\nD2000000 00000000";
    const string DPDE = "94000130 FCFF0000\nB21C4E68 00000000\nB0000004 00000000\n00000DAC 03E70032\nD2000000 00000000";
    const string PtDE = "94000130 FCFF0000\nB2101EE0 00000000\n00000B60 03E70032\nD2000000 00000000";
    const string PtEN = "94000130 FCFF0000\nB2101D40 00000000\n00000B60 03E70032\nD2000000 00000000";
    const string HgssDE = "94000130 FCFF0000\n62111860 00000000\nB2111860 00000000\n00000B74 03E70032\nD2000000 00000000";
    const string HgssEN = "94000130 FCFF0000\n62111880 00000000\nB2111880 00000000\n00000B74 03E70032\nD2000000 00000000";
    const string BW = "94000130 FFFB0000\nB2000024 00000000\n00019494 03E70032\nD2000000 00000000";
    const string B2W2 = "94000130 FFFB0000\nB2000024 00000000\n000194F8 03E70032\nD2000000 00000000";

    static readonly Dictionary<string, Code> BuiltIn = new()
    {
        // Diamant / Perl: the English US (header version 5) and EU (version 13) releases share the game code and the pointer.
        ["ADAE"] = new("Diamant (EN)", DP, "L + R", true),
        ["APAE"] = new("Perl (EN)", DP, "L + R", true),
        ["ADAD"] = new("Diamant (DE)", DPDE, "L + R", true),
        ["APAD"] = new("Perl (DE)", DPDE, "L + R", true),
        // Platin / HeartGold / SoulSilver: the English Europe releases (header version 10) share code and pointer with the US ones.
        ["CPUD"] = new("Platin (DE)", PtDE, "L + R", true),
        ["CPUE"] = new("Platin (EN)", PtEN, "L + R", true),
        ["IPKD"] = new("HeartGold (DE)", HgssDE, "L + R", true),
        ["IPGD"] = new("SoulSilver (DE)", HgssDE, "L + R", true),
        ["IPKE"] = new("HeartGold (EN)", HgssEN, "L + R", true),
        ["IPGE"] = new("SoulSilver (EN)", HgssEN, "L + R", true),
        // Black/White and Black 2/White 2: one pointer for all languages; "O" = English (US + Europe).
        ["IRBD"] = new("Schwarz (DE)", BW, "Select", true),
        ["IRAD"] = new("Weiß (DE)", BW, "Select", true),
        ["IRBO"] = new("Black (EN)", BW, "Select", true),
        ["IRAO"] = new("White (EN)", BW, "Select", true),
        ["IRED"] = new("Schwarz 2 (DE)", B2W2, "Select", true),
        ["IRDD"] = new("Weiß 2 (DE)", B2W2, "Select", true),
        ["IREO"] = new("Black 2 (EN)", B2W2, "Select", true),
        ["IRDO"] = new("White 2 (EN)", B2W2, "Select", true),
    };

    /// <summary>The code for this ROM: own code first, then a built-in one matching game code (and version where needed).</summary>
    public static Code? Get(AppConfig cfg, RomInfo rom)
    {
        if (cfg.CustomCheats.TryGetValue(rom.GameCode, out var custom) && !string.IsNullOrWhiteSpace(custom))
            return new Code("Eigener Code", custom, ActivationFromCode(custom), true);
        if (!BuiltIn.TryGetValue(rom.GameCode, out var code)) return null;
        return code.RomVersion == null || code.RomVersion == rom.RomVersion ? code : null;
    }

    /// <summary>Explains why a built-in code exists for the game code but doesn't fit this ROM (other region, same code).</summary>
    public static string? VersionMismatch(AppConfig cfg, RomInfo rom)
    {
        if (Get(cfg, rom) != null || !BuiltIn.TryGetValue(rom.GameCode, out var code) || code.RomVersion == null) return null;
        var region = rom.RomVersion == 13 ? "Europa-Version" : $"eine andere Version (Header-Version {rom.RomVersion})";
        return $"Der eingebaute Code ist nur für die US-Version – deine ROM ist die {region}, deshalb wird er nicht benutzt. "
             + "Einen passenden Code kannst du in den Einstellungen eintragen.";
    }

    public static string? GetBuiltInText(string gameCode) =>
        BuiltIn.TryGetValue(gameCode, out var c) ? c.Codes : null;

    public static string? GetBuiltInNote(string gameCode) =>
        BuiltIn.TryGetValue(gameCode, out var c) && c.RomVersion == 5
            ? "Gilt nur für die US-Version (nicht für die englische Europa-Version mit demselben Spielcode)."
            : null;

    /// <summary>Reads the button condition (94000130 XXXX0000) so the UI can tell which buttons to press.</summary>
    static string ActivationFromCode(string code)
    {
        var m = Regex.Match(code, @"94000130\s*([0-9A-Fa-f]{4})0000");
        if (!m.Success) return "immer aktiv";
        int mask = Convert.ToInt32(m.Groups[1].Value, 16);
        string[] names = ["A", "B", "Select", "Start", "Rechts", "Links", "Hoch", "Runter", "R", "L"];
        var pressed = Enumerable.Range(0, 10).Where(bit => (mask & (1 << bit)) == 0).Select(bit => names[bit]);
        return string.Join(" + ", pressed.Reverse());
    }

    /// <summary>Normalizes user input ("94000130 FCFF0000" per line) into 16-hex-char code lines.</summary>
    public static List<string> ParseLines(string code)
    {
        var hex = Regex.Replace(code, @"[^0-9A-Fa-f]", "").ToUpperInvariant();
        if (hex.Length == 0 || hex.Length % 16 != 0)
            throw new FormatException("Der Code muss aus Paaren von 8-stelligen Hex-Werten bestehen (z. B. \"94000130 FCFF0000\").");
        return Enumerable.Range(0, hex.Length / 16).Select(i => hex.Substring(i * 16, 16)).ToList();
    }
}

/// <summary>A supported emulator install: where it keeps saves/cheats and how to write a cheat file.</summary>
public interface IEmulator
{
    string Name { get; }
    string Folder { get; }
    string? Exe { get; }

    /// <summary>True if the emulator already has a save or cheat file for a ROM with this name (ROMs in <paramref name="romFolder"/>).</summary>
    bool HasDataFor(string romFolder, string romBaseName);

    /// <summary>Writes a cheat file that is active when the ROM starts. Returns the file and an optional hint for the user.</summary>
    (string File, string? Note) WriteCheatFile(string romPath, RomInfo rom, List<string> codeLines, string description);
}

public static class Emulators
{
    /// <summary>Recognises the emulator by its exe in the folder (melonDS or DeSmuME); null if neither is there.</summary>
    public static IEmulator? TryOpen(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        if (Directory.GetFiles(folder, "melonDS*.exe").Length > 0) return new MelonDSInstall(folder);
        if (Directory.GetFiles(folder, "DeSmuME*.exe").Length > 0) return new DeSmuMEInstall(folder);
        return null;
    }
}

/// <summary>Knows where DeSmuME keeps battery saves and cheat files, and writes .dct cheat files.</summary>
public class DeSmuMEInstall : IEmulator
{
    public string Name => "DeSmuME";
    public string Folder { get; }
    public string? Exe { get; }
    public string BatteryDir { get; }
    public string CheatsDir { get; }

    public DeSmuMEInstall(string folder)
    {
        Folder = folder;
        var exes = Directory.GetFiles(folder, "DeSmuME*.exe");
        Exe = exes.FirstOrDefault(e => e.Contains("x64", StringComparison.OrdinalIgnoreCase)) ?? exes.FirstOrDefault();

        var paths = ReadPathSettings();
        BatteryDir = Resolve(paths.GetValueOrDefault("Battery", @".\Battery"));
        CheatsDir = Resolve(paths.GetValueOrDefault("Cheats", @".\Cheats"));
    }

    string Resolve(string p) => Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(Folder, p));

    /// <summary>[PathSettings] from the ini next to the used exe (DeSmuME stores relative paths there).</summary>
    Dictionary<string, string> ReadPathSettings()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ini = Exe != null ? Path.ChangeExtension(Exe, ".ini") : null;
        if (ini == null || !File.Exists(ini))
            ini = Directory.GetFiles(Folder, "*.ini").FirstOrDefault();
        if (ini == null) return result;

        bool inSection = false;
        foreach (var raw in File.ReadLines(ini))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals("[PathSettings]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            int eq = line.IndexOf('=');
            if (inSection && eq > 0)
                result[line[..eq]] = line[(eq + 1)..];
        }
        return result;
    }

    public bool HasDataFor(string romFolder, string romBaseName) =>
        File.Exists(Path.Combine(BatteryDir, romBaseName + ".dsv")) ||
        File.Exists(Path.Combine(CheatsDir, romBaseName + ".dct"));

    /// <summary>DeSmuME loads Cheats\&lt;rom file name&gt;.dct automatically when the ROM starts.</summary>
    public (string File, string? Note) WriteCheatFile(string romPath, RomInfo rom, List<string> codeLines, string description)
    {
        Directory.CreateDirectory(CheatsDir);
        var file = Path.Combine(CheatsDir, Path.GetFileNameWithoutExtension(romPath) + ".dct");
        var platform = rom.UnitCode >= 2 ? "TWL" : "NTR";
        var sb = new StringBuilder();
        sb.Append("; DeSmuME cheats file. VERSION 2.000\r\n");
        sb.Append($"Name={rom.HeaderTitle}\r\n");
        sb.Append($"Serial={platform}-{rom.GameCode}-{RegionCode(rom.GameCode)}\r\n");
        sb.Append("\r\n; cheats list\r\n");
        sb.Append($"AR 1 {string.Join(",", codeLines)} ;{description}\r\n\r\n");
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        return (file, null);
    }

    static string RegionCode(string gameCode) => gameCode.Length < 4 ? "EUR" : gameCode[3] switch
    {
        'D' or 'F' => "NOE",
        'E' or 'O' => "USA",
        'J' => "JPN",
        'K' => "KOR",
        'I' => "ITA",
        'S' => "SPA",
        _ => "EUR",
    };
}
