using System.Diagnostics;
using System.IO;

namespace RandoApp;

/// <summary>
/// Running Universal Pokemon Randomizer ZX and reading what it did ("Neuer Run").
/// </summary>
public static class RandomizerProcess
{
    /// <summary>
    /// Runs PokeRandoZX (through RandoHelper when present) and returns its exit code. Every output line goes to
    /// <paramref name="log"/> – from a background thread, the caller brings it to its UI.
    /// </summary>
    public static Task<int> Run(AppConfig cfg, string romPath, string outPath, Action<string> log)
    {
        var psi = new ProcessStartInfo(cfg.ResolveJava())
        {
            WorkingDirectory = Path.GetDirectoryName(cfg.RandomizerJar)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add($"-Xmx{cfg.JavaMaxMemoryMb}M");
        // RandoHelper wraps the CLI and, like the GUI, disables options the ROM doesn't support
        // (the plain CLI crashes e.g. with "Correct Static Music" on German Black/White).
        var helperJar = Path.Combine(AppContext.BaseDirectory, "RandoHelper.jar");
        if (File.Exists(helperJar))
        {
            psi.ArgumentList.Add("-cp");
            psi.ArgumentList.Add($"{cfg.RandomizerJar}{Path.PathSeparator}{helperJar}");
            psi.ArgumentList.Add("RandoHelper");
        }
        else
        {
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add(cfg.RandomizerJar);
        }
        psi.ArgumentList.Add("cli");
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(cfg.SettingsFile);
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(romPath);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outPath);
        psi.ArgumentList.Add("-l"); // always: the starter/static summary is read from it; deleted afterwards unless wanted

        var tcs = new TaskCompletionSource<int>();
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        void OnLine(object _, DataReceivedEventArgs a)
        {
            if (a.Data != null) log(a.Data);
        }
        proc.OutputDataReceived += OnLine;
        proc.ErrorDataReceived += OnLine;
        proc.Exited += (_, _) =>
        {
            proc.WaitForExit(); // flush remaining output
            int exit = proc.ExitCode;
            proc.Dispose();
            if (exit == 0) TakeAppendedName(outPath);
            tcs.TrySetResult(exit);
        };
        try
        {
            proc.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"Java wurde nicht gefunden ('{psi.FileName}'). Java installieren oder Pfad in den Einstellungen setzen.");
        }
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        return tcs.Task;
    }

    /// <summary>
    /// The randomizer saves with the extension it thinks right and appends it when the name has another one: a run of
    /// Rot/Blau/Gelb asked for as "….gb" comes out as "….gb.gbc" (with "….gb.gbc.log"). The file gets the name that
    /// was asked for, so the run is found – same game data, mGBA plays it either way.
    /// </summary>
    static void TakeAppendedName(string outPath)
    {
        try
        {
            if (File.Exists(outPath)) return;
            var dir = Path.GetDirectoryName(outPath)!;
            var made = Directory.GetFiles(dir, Path.GetFileName(outPath) + ".*")
                .Where(f => !f.EndsWith(".log", StringComparison.OrdinalIgnoreCase)).ToList();
            if (made.Count != 1) return;
            File.Move(made[0], outPath);
            if (File.Exists(made[0] + ".log") && !File.Exists(outPath + ".log")) File.Move(made[0] + ".log", outPath + ".log");
        }
        catch (IOException)
        {
            // left as it is – the caller reports the run as failed
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Starters and statics from the randomizer's log, saved as "&lt;rom&gt;.spoiler.txt" if wanted; null without a log.
    /// The full log is kept only if enabled. <paramref name="gameCode"/>: for ROMs without one in the header (3DS).
    /// </summary>
    public static string? ReadSpoiler(AppConfig cfg, RomInfo rom, string outPath, Action<string> log, string? gameCode = null)
    {
        var logPath = outPath + ".log";
        if (!File.Exists(logPath)) return null;
        try
        {
            var text = SpoilerSummary.Build(logPath, rom, gameCode);
            if (cfg.CreateSpoilerFile)
            {
                var spoilerPath = Path.ChangeExtension(outPath, ".spoiler.txt");
                File.WriteAllText(spoilerPath, text);
                log($"Spoiler: {spoilerPath}");
            }
            return text;
        }
        catch (Exception ex)
        {
            log("Spoiler konnte nicht gelesen werden: " + ex.Message);
            return null;
        }
        finally
        {
            // a locked log (virus scanner) stays behind – the new ROM is done either way
            try { if (!cfg.CreateLog) File.Delete(logPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
