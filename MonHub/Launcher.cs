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

    /// <summary>
    /// "Öffnen" on the Emulators page: the emulator on its own (its settings, a game of the player's choice). Already open
    /// – inside MonHub or in a window – a second one could write over the first one's saves, so that is asked first.
    /// </summary>
    public static bool OpenEmulator(Window? owner, string? exe, string emulator)
    {
        if (exe != null && IsRunning(emulator))
        {
            bool inside = (Compat.MainWindow as MainWindow)?.GameEmulator == emulator;
            int choice = MessageBox.Choose(owner, Txt.L($"{emulator} ist schon offen{(inside ? " – mit dem Spiel unter „Weiter“" : "")}. Zwei offene {emulator} können sich gegenseitig den Spielstand überschreiben.",
                    $"{emulator} is already open{(inside ? " – with the game under “Continue”" : "")}. Two open {emulator} can overwrite each other’s saves."),
                Txt.L("Emulator schon offen", "Emulator already open"), MessageBoxImage.Warning,
                inside ? [Txt.L("Zum Spiel", "To the game"), Txt.L("Trotzdem öffnen", "Open anyway"), Txt.L("Abbrechen", "Cancel")]
                       : [Txt.L("Trotzdem öffnen", "Open anyway"), Txt.L("Abbrechen", "Cancel")],
                enter: inside ? 0 : 1);
            if (inside && choice == 0) Shell.Navigate("start");
            if (choice != (inside ? 1 : 0)) return false;
        }
        return Start(owner, exe);
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
            return Play(owner, HubPaths.AzaharExe, rom, GameLibrary.Azahar);
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
        return exe != null ? Play(owner, exe, rom, emulator) : Start(owner, rom);
    }

    /// <summary>
    /// An emulator with a game: inside MonHub (the "Weiter" page shows the game) unless games should open in their own
    /// window – or MonHub can't take windows on this system.
    /// </summary>
    public static bool Play(Window? owner, string exe, string rom, string? emulator)
    {
        if (!File.Exists(exe)) return Start(owner, exe);
        var main = Compat.MainWindow as MainWindow;
        bool inside = !HubConfig.Current.GamesInOwnWindow && main != null && GameScreen.Possible;
        if (inside && main!.GameRom is { } running)
        {
            // one game at a time inside MonHub: back to it, or quit it for the new one (Enter: back – nothing is lost)
            string now = Path.GetFileNameWithoutExtension(running), next = Path.GetFileNameWithoutExtension(rom);
            int choice = running == rom ? 0 : MessageBox.Choose(owner, Txt.L($"Gerade ist „{now}“ offen.\n\nZurück zu diesem Spiel – oder es beenden und „{next}“ starten? Beim Beenden ist weg, was du in „{now}“ nicht gespeichert hast.",
                    $"“{now}” is open right now.\n\nBack to that game – or quit it and start “{next}”? Quitting loses whatever you haven’t saved in “{now}”."),
                Txt.L("Ein Spiel läuft schon", "A game is already running"), MessageBoxImage.Information,
                [Txt.L("Zum laufenden Spiel", "Back to the running game"), Txt.L("Beenden und neues starten", "Quit it and start the new one"), Txt.L("Abbrechen", "Cancel")]);
            if (choice == 0) main.Navigate("start");
            else if (choice == 1) main.SwitchGame(rom, emulator);
            return false;
        }
        // the same emulator already open in a window of its own (e.g. sent there with "Eigenes Fenster"): two of them can
        // write over each other's saves
        if (emulator != null && IsRunning(emulator) && main?.GameEmulator != emulator
            && MessageBox.Choose(owner, Txt.L($"{emulator} ist schon offen – in einem eigenen Fenster. Zwei offene Spiele im selben Emulator können sich gegenseitig den Spielstand überschreiben.\n\nAm besten speicherst du dort und schließt es zuerst.",
                    $"{emulator} is already open – in a window of its own. Two games open in the same emulator can overwrite each other’s saves.\n\nBest save there and close it first."),
                Txt.L("Emulator schon offen", "Emulator already open"), MessageBoxImage.Warning,
                [Txt.L("Trotzdem starten", "Start anyway"), Txt.L("Abbrechen", "Cancel")], enter: 1) != 0)
            return false;
        // DeSmuME holds its proportions by moving its own window – inside MonHub that throws it off the screen,
        // so there MonHub keeps the proportions instead
        if (emulator == GameLibrary.DeSmuME) HubSetup.SetDeSmuMERatio(fixedByDeSmuME: !inside);
        // inside MonHub: the screens arranged as chosen under "Emulatoren" (or what fits the room best), and the picture's
        // proportions, so the window gets just the room it needs
        double? ratio = null;
        if (inside)
        {
            bool threeDs = emulator == GameLibrary.Azahar, ds = emulator is GameLibrary.MelonDS or GameLibrary.DeSmuME;
            if (threeDs || ds)
            {
                var arrangement = ScreenLayout.Resolve(threeDs ? HubConfig.Current.ThreeDsScreens : HubConfig.Current.DsScreens, threeDs, main!.GameAreaRatio);
                if (ds && emulator == GameLibrary.DeSmuME && arrangement == ScreenArrangement.BigAndSmall) arrangement = ScreenArrangement.SideBySide;
                ScreenLayout.Apply(emulator, arrangement);
                ratio = ScreenLayout.Ratio(threeDs, arrangement);
            }
            else if (emulator == GameLibrary.MGBA)
                ratio = ScreenLayout.GameBoyRatio(rom);
        }
        try
        {
            var process = Os.Start(exe, $"\"{rom}\"", Path.GetDirectoryName(exe)!, quiet: inside);
            if (inside && process != null) main!.Embed(process, rom, emulator, ratio);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "MonHub", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
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
                // one that has just ended (still on the list for a moment) can't name its file any more – it doesn't count
                if (process.HasExited) continue;
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
