using System.Diagnostics;
using System.IO;

namespace MonHub;

/// <summary>Starts programs and games – a problem shows up as a message instead of a crash.</summary>
public static class Launcher
{
    public static bool Start(Window? owner, string? file, string? args = null, string? workingDir = null)
    {
        if (file == null || !File.Exists(file))
        {
            MessageBox.Show(owner, Txt.L($"Nicht gefunden:\n{file}", $"Not found:\n{file}"), "MonHub", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        try
        {
            Os.Start(file, args, workingDir ?? Path.GetDirectoryName(file)!);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "MonHub", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>A game: DS games in the given emulator, everything else with the program Windows uses for the file.</summary>
    public static bool StartGame(Window? owner, string rom, string? emulator)
    {
        // 3DS: Azahar, pointed at this game's own SD card (saves) and cheats first
        if (Rom3ds.IsFile(rom))
        {
            if (HubPaths.AzaharExe == null)
            {
                MessageBox.Show(owner, Txt.L("Azahar (3DS-Emulator) fehlt – bitte MonHub neu installieren.", "Azahar (3DS emulator) is missing – please reinstall MonHub."), "MonHub", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            string? problem;
            try { problem = Azahar3ds.Prepare(rom); }
            catch (Exception ex) { problem = Txt.L("Azahar konnte nicht vorbereitet werden: ", "Azahar couldn't be prepared: ") + ex.Message; }
            if (problem != null)
            {
                MessageBox.Show(owner, problem, "MonHub", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return Start(owner, HubPaths.AzaharExe, $"\"{rom}\"");
        }
        // melonDS would only show a white screen with an encrypted ROM – DeSmuME decrypts it itself
        if (emulator == GameLibrary.MelonDS && HubPaths.DeSmuMEExe != null && !Bios.Installed && RomCheck.HasEncryptedSecureArea(rom))
            emulator = GameLibrary.DeSmuME;
        // … and where there is no DeSmuME (Linux), say what is missing instead of showing that white screen
        if (emulator == GameLibrary.MelonDS && HubPaths.DeSmuMEExe == null && !Bios.Installed && RomCheck.HasEncryptedSecureArea(rom))
        {
            MessageBox.Show(owner, Txt.L(
                "Dieses DS-Spiel ist noch verschlüsselt (wie viele US-Versionen). melonDS startet es nur mit deinen eigenen DS-BIOS-Dateien – " +
                "spiel sie unter „Emulatoren“ → „BIOS einspielen …“ ein. MonHub bringt kein BIOS mit.",
                "This DS game is still encrypted (like many US versions). melonDS only starts it with your own DS BIOS files – " +
                "add them under “Emulators” → “Add BIOS …”. MonHub doesn't include a BIOS."), "MonHub", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (emulator == GameLibrary.DeSmuME) CarryCheatToDeSmuME(rom);
        // Game Boy, Game Boy Color and GBA: in mGBA
        if (emulator == null && HubPaths.MGBAExe != null && Path.GetExtension(rom).ToLowerInvariant() is ".gba" or ".gb" or ".gbc")
            emulator = GameLibrary.MGBA;
        var exe = GameLibrary.ExeFor(emulator);
        return exe != null ? Start(owner, exe, $"\"{rom}\"") : Start(owner, rom);
    }

    /// <summary>
    /// A run made for melonDS keeps its cheat (Rare Candy) in "&lt;rom&gt;.mch". Starting it in DeSmuME, the same codes go into
    /// DeSmuME's cheat file – once; a DeSmuME cheat file that already exists is left alone.
    /// </summary>
    static void CarryCheatToDeSmuME(string rom)
    {
        var mch = Path.ChangeExtension(rom, ".mch");
        if (!File.Exists(mch) || HubPaths.DeSmuMEExe == null) return;
        try
        {
            var desmume = new RandoApp.DeSmuMEInstall(HubPaths.DeSmuME);
            if (File.Exists(Path.Combine(desmume.CheatsDir, Path.GetFileNameWithoutExtension(rom) + ".dct"))) return;
            var codes = File.ReadLines(mch).Select(l => l.Trim())
                .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, "^[0-9A-Fa-f]{8} [0-9A-Fa-f]{8}$"));
            var text = string.Join(" ", codes);
            if (text.Length == 0) return;
            desmume.WriteCheatFile(rom, new RandoApp.RomInfo(rom), RandoApp.RareCandyCodes.ParseLines(text), "Rare Candy x999");
        }
        catch
        {
            // no cheat is better than no game
        }
    }

    /// <summary>Only MonHub's own copy counts – an emulator the player runs from somewhere else doesn't touch these saves.</summary>
    public static bool IsRunning(string emulator) => Os.Windows
        ? IsProgramRunning(GameLibrary.ExeFor(emulator))
        // Linux: the start file is "AppRun" for all of them – go by the program's own name
        : GameLibrary.ExeFor(emulator) != null && HubSetup.IsRunning(emulator);

    /// <summary>Whether this exact program (by path) is running.</summary>
    public static bool IsProgramRunning(string? exe)
    {
        if (exe == null) return false;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
                return true; // can't tell – better safe
            }
            finally
            {
                process.Dispose();
            }
        }
        return false;
    }

    /// <summary>
    /// An open emulator would write its save back when it closes – so deleting waits until both are closed.
    /// Shows the reason and returns true while one is still open.
    /// </summary>
    public static bool EmulatorsOpen(Window? owner, string title)
    {
        // mGBA too: it keeps the save in memory and writes it back – a deleted GBA save would come back
        if (!IsRunning(GameLibrary.MelonDS) && !IsRunning(GameLibrary.DeSmuME) && !IsRunning(GameLibrary.Azahar) && !IsRunning(GameLibrary.MGBA)) return false;
        MessageBox.Show(owner, Txt.L("Bitte schließ zuerst die Emulatoren (melonDS, DeSmuME, mGBA, Azahar) – ein offenes Spiel würde seinen Spielstand beim Beenden wieder hinschreiben.",
                "Please close the emulators first (melonDS, DeSmuME, mGBA, Azahar) – an open game would write its save back when it closes."),
            title, MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
    }
}
