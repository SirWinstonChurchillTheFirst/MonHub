using System.Diagnostics;
using System.IO;
using System.Text;

using Txt = MonHub.Txt;

namespace RandoApp;

/// <summary>
/// melonDS (1.x): saves (.sav) and cheats (.mch) live next to the ROM unless SaveFilePath / CheatFilePath are set in
/// melonDS.toml. Cheats only run with [Instance0] EnableCheats = true.
/// </summary>
public class MelonDSInstall : IEmulator
{
    public string Name => "melonDS";
    public string Folder { get; }
    public string? Exe { get; }
    string ConfigFile => Path.Combine(Folder, "melonDS.toml");

    public MelonDSInstall(string folder)
    {
        Folder = folder;
        Exe = Directory.GetFiles(folder, "melonDS*.exe").FirstOrDefault();
    }

    /// <summary>A value from the [Instance0] section of melonDS.toml ("" if missing).</summary>
    string InstanceSetting(string key)
    {
        if (!File.Exists(ConfigFile)) return "";
        bool inSection = false;
        foreach (var raw in File.ReadLines(ConfigFile))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inSection = line == "[Instance0]"; continue; }
            if (inSection && line.StartsWith(key + " ", StringComparison.Ordinal))
                return line[(line.IndexOf('=') + 1)..].Trim().Trim('"');
        }
        return "";
    }

    /// <summary>Folder for saves/cheats: the configured one, or (default) the ROM's own folder.</summary>
    string DirFor(string settingKey, string romFolder)
    {
        var dir = InstanceSetting(settingKey);
        if (dir.Length == 0) return romFolder;
        return Path.IsPathRooted(dir) ? dir : Path.GetFullPath(Path.Combine(Folder, dir));
    }

    public bool HasDataFor(string romFolder, string romBaseName) =>
        File.Exists(Path.Combine(DirFor("SaveFilePath", romFolder), romBaseName + ".sav")) ||
        File.Exists(Path.Combine(DirFor("CheatFilePath", romFolder), romBaseName + ".mch"));

    /// <summary>melonDS cheat file: a "CAT" category with "CODE &lt;enabled&gt; &lt;name&gt;" entries and "XXXXXXXX YYYYYYYY" lines.</summary>
    public (string File, string? Note) WriteCheatFile(string romPath, RomInfo rom, List<string> codeLines, string description)
    {
        var dir = DirFor("CheatFilePath", Path.GetDirectoryName(romPath)!);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Path.GetFileNameWithoutExtension(romPath) + ".mch");
        var sb = new StringBuilder();
        sb.Append("CAT 0 MonHub\n\n");
        sb.Append($"CODE 1 {description}\n");
        foreach (var line in codeLines)
            sb.Append($"{line[..8]} {line[8..]}\n");
        sb.Append('\n');
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        return (file, EnsureCheatsEnabled());
    }

    /// <summary>Turns on EnableCheats in melonDS.toml – only while melonDS is closed, since it rewrites the file on exit.</summary>
    string? EnsureCheatsEnabled()
    {
        if (!File.Exists(ConfigFile) || InstanceSetting("EnableCheats") == "true") return null;
        if (Process.GetProcessesByName("melonDS").Length > 0)
            return Txt.L("In melonDS sind Cheats noch ausgeschaltet – melonDS schließen und nochmal randomisieren, oder in melonDS unter „System“ die Cheats einschalten.",
                "Cheats are still switched off in melonDS – close melonDS and randomize again, or switch cheats on in melonDS under “System”.");

        var lines = File.ReadAllLines(ConfigFile).ToList();
        bool inSection = false, done = false;
        for (int i = 0; i < lines.Count && !done; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[')) { inSection = t == "[Instance0]"; continue; }
            if (inSection && t.StartsWith("EnableCheats ", StringComparison.Ordinal))
            {
                lines[i] = "EnableCheats = true";
                done = true;
            }
        }
        if (!done)
        {
            int at = lines.FindIndex(l => l.Trim() == "[Instance0]");
            if (at < 0) return Txt.L("In melonDS unter „System“ die Cheats einschalten.", "Switch cheats on in melonDS under “System”.");
            lines.Insert(at + 1, "EnableCheats = true");
        }
        SafeFile.WriteAllLines(ConfigFile, lines); // UTF-8 without BOM, never half-written
        return Txt.L("Cheats in melonDS wurden eingeschaltet.", "Cheats were switched on in melonDS.");
    }
}
