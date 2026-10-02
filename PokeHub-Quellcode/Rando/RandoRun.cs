using System.Diagnostics;
using System.IO;

namespace RandoApp;

/// <summary>
/// The steps of one randomization, without any window (used by the "Neuer Run" page), so they
/// name, randomize, cheat and spoil the same way. <c>log</c> may be called from any thread.
/// </summary>
public sealed class RandoRun(AppConfig cfg, Action<string> log)
{
    public static string? Validate(AppConfig cfg, RomInfo? rom)
    {
        if (rom == null) return "Keine ROM ausgewählt. Prüfe den ROM-Ordner in den Einstellungen.";
        if (!File.Exists(rom.Path)) return "Die ausgewählte ROM existiert nicht mehr.";
        if (PokeHub.Rom3ds.IsFile(rom.Path) && PokeHub.Rom3ds.Read(rom.Path) is not { Encrypted: false })
            return "Dieses 3DS-Spiel ist verschlüsselt (oder keins) – der Randomizer und Azahar brauchen ein entschlüsseltes Spiel.";
        if (!File.Exists(cfg.RandomizerJar)) return "PokeRandoZX.jar nicht gefunden – Pfad in den Einstellungen setzen.";
        if (!File.Exists(cfg.SettingsFile)) return "Settings-Datei (.rnqs) nicht gefunden – in den Einstellungen auswählen.";
        if (string.IsNullOrWhiteSpace(cfg.OutputFolder)) return "Kein Ausgabe-Ordner gesetzt.";
        return null;
    }

    /// <summary>A name that is free in the output folder AND in the emulator's saves/cheats, so no old save gets picked up.</summary>
    public static bool IsNameTaken(AppConfig cfg, string baseName, string ext, IEmulator? emu) =>
        File.Exists(Path.Combine(cfg.OutputFolder, baseName + ext)) ||
        File.Exists(Path.Combine(cfg.OutputFolder, baseName + ext + ".log")) ||
        File.Exists(Path.Combine(cfg.OutputFolder, baseName + ".spoiler.txt")) ||
        Directory.Exists(PokeHub.Azahar3ds.SdCardOf(baseName)) || // 3DS saves of an older game with that name
        (emu?.HasDataFor(cfg.OutputFolder, baseName) ?? false);

    public static string UniqueBaseName(AppConfig cfg, string wanted, string ext, IEmulator? emu)
    {
        var name = wanted;
        for (int i = 2; IsNameTaken(cfg, name, ext, emu); i++)
            name = $"{wanted}_{i}";
        return name;
    }

    /// <summary>The default name of a new run: ROM name + date/time.</summary>
    public static string DefaultBaseName(RomInfo rom) => $"{Path.GetFileNameWithoutExtension(rom.Path)}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

    /// <summary>What the Rare Candy cheat will do for this ROM: hint text, whether it can be used, whether it is a warning.</summary>
    public static (string Hint, bool Available, bool Warning) CheatHint(AppConfig cfg, RomInfo? rom)
    {
        if (rom == null) return ("", false, false);
        if (PokeHub.Rom3ds.IsFile(rom.Path))
            return RareCandyCode3ds(rom) != null
                ? ("Gen 6/7: 999 Sonderbonbons im ersten Platz der Medizin-Tasche, sobald du den Beutel hast (Cheat in Azahar).", true, false)
                : ("Für dieses 3DS-Spiel gibt es keinen Rare-Candy-Code (nur X/Y, ΩR/αS, Sonne/Mond, Ultrasonne/Ultramond).", false, true);
        if (rom.Platform is RomPlatform.Gb or RomPlatform.Gba)
            return RareCandyPatch.CanPatch(rom.Path, rom.Platform)
                ? ("Gen 1–3: 99 Sonderbonbons liegen zu Spielbeginn im PC deines Zimmers.", true, false)
                : ("Für diese ROM geht kein Rare Candy – die Stelle im Spiel wurde nicht eindeutig gefunden (Hack oder unbekannte Version).", false, true);
        if (rom.Platform != RomPlatform.Nds) return ("Rare-Candy-Cheat gibt es nur für DS-, GBA- und Game-Boy-Spiele.", false, true);
        if (RareCandyCodes.Get(cfg, rom) is { } code)
            return Emulators.TryOpen(cfg.EmulatorFolder) == null
                ? ("Emulator-Ordner (DeSmuME oder melonDS) in den Einstellungen setzen, sonst kann die Cheat-Datei nicht angelegt werden.", true, true)
                : ($"{code.Game}: Im Spiel {code.Activation} drücken → 999 Sonderbonbons in der Medizin-Tasche.", true, false);
        return (RareCandyCodes.VersionMismatch(cfg, rom)
                ?? $"Für diese Version ({rom.GameCode}) gibt es noch keinen Rare-Candy-Code – sag Bescheid, dann wird einer ermittelt. "
                 + "Einen eigenen Code kannst du auch in den Einstellungen eintragen.", false, true);
    }

    /// <summary>Runs PokeRandoZX (through RandoHelper when present) and returns its exit code; output goes to the log.</summary>
    public Task<int> RunRandomizer(string romPath, string outPath) => RandomizerProcess.Run(cfg, romPath, outPath, log);

    /// <summary>Gives the new ROM (and its log) another name; returns the new path.</summary>
    public string Rename(string outPath, string newBaseName)
    {
        var newPath = Path.Combine(Path.GetDirectoryName(outPath)!, newBaseName + Path.GetExtension(outPath));
        File.Move(outPath, newPath);
        if (File.Exists(PokeHub.Azahar3ds.CheatFileOf(outPath)))
            File.Move(PokeHub.Azahar3ds.CheatFileOf(outPath), PokeHub.Azahar3ds.CheatFileOf(newPath));
        if (File.Exists(outPath + ".log"))
            File.Move(outPath + ".log", newPath + ".log");
        log($"Umbenannt in: {Path.GetFileName(newPath)}");
        return newPath;
    }

    /// <summary>Starters and statics from the randomizer's log, saved as "&lt;rom&gt;.spoiler.txt" if wanted; null without a log.
    /// The full log is kept only if enabled.</summary>
    public string? Spoiler(RomInfo rom, string outPath) =>
        RandomizerProcess.ReadSpoiler(cfg, rom, outPath, log, rom.GameCode.Length > 0 ? null : PokeHub.Rom3ds.Read(rom.Path)?.GameCode);

    static string? RareCandyCode3ds(RomInfo rom) => PokeHub.Rom3ds.Read(rom.Path) is { } info ? PokeHub.Azahar3ds.RareCandyCode(info.GameCode) : null;

    /// <summary>Writes the Rare Candy cheat file for the emulator; returns the line for the status.</summary>
    public string ApplyCheat(RomInfo rom, string outPath, IEmulator? emu)
    {
        if (PokeHub.Rom3ds.IsFile(rom.Path))
        {
            if (RareCandyCode3ds(rom) is not { } code3ds) return "Rare Candy: für dieses 3DS-Spiel gibt es keinen Code – übersprungen.";
            var cheatFile = PokeHub.Azahar3ds.CheatFileOf(outPath);
            File.WriteAllText(cheatFile, PokeHub.Azahar3ds.CheatText(code3ds));
            log($"Cheat-Datei (Azahar): {cheatFile}");
            return "Rare Candy aktiv – 999 Sonderbonbons in der Medizin-Tasche.";
        }
        if (rom.Platform is RomPlatform.Gb or RomPlatform.Gba)
        {
            bool patched = RareCandyPatch.Apply(outPath, rom.Platform);
            log(patched ? "Rare Candy: 99 Sonderbonbons im Start-PC eingetragen." : "Rare Candy: Stelle nicht gefunden – übersprungen.");
            return patched ? "Rare Candy aktiv – 99 Sonderbonbons liegen zu Spielbeginn im PC." : "Rare Candy übersprungen – die Stelle im Spiel wurde nicht gefunden.";
        }
        if (rom.Platform != RomPlatform.Nds)
            return "Rare-Candy-Cheat: nur für DS-, GBA- und Game-Boy-Spiele – übersprungen.";
        if (emu == null)
            return "Rare-Candy-Cheat: kein Emulator (DeSmuME/melonDS) eingestellt – übersprungen.";
        var code = RareCandyCodes.Get(cfg, rom);
        if (code == null)
            return RareCandyCodes.VersionMismatch(cfg, rom) is { } why
                ? "Rare-Candy-Cheat übersprungen: " + why
                : $"Rare-Candy-Cheat: kein Code für {rom.GameCode} hinterlegt – übersprungen.";

        var (file, note) = emu.WriteCheatFile(outPath, rom, RareCandyCodes.ParseLines(code.Codes), "Rare Candy x999");
        log($"Cheat-Datei ({emu.Name}): {file}");
        if (note != null) log(note);
        return $"Rare Candy Cheat aktiv – im Spiel {code.Activation} drücken." + (note != null ? " " + note : "");
    }
}
