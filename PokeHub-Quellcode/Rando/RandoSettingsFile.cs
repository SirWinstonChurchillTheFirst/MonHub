using System.Diagnostics;
using System.IO;
using System.Text;

using Txt = PokeHub.Txt;

namespace RandoApp;

/// <summary>One option of a randomizer settings file (.rnqs), as reported by RandoHelper settings-dump.</summary>
public record RandoOption(string Name, string Type, string Value, string[] Choices);

/// <summary>Reads/writes .rnqs files through the randomizer's own Settings class (via RandoHelper.jar).</summary>
public static class RandoSettingsFile
{
    public static async Task<List<RandoOption>> LoadAsync(AppConfig cfg, string path)
    {
        var output = await RunHelperAsync(cfg, "settings-dump", path);
        var result = new List<RandoOption>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length < 4) continue;
            var choices = parts[3].Length == 0 ? [] : parts[3].Split(',');
            result.Add(new RandoOption(parts[0], parts[1], parts[2], choices));
        }
        return result;
    }

    /// <summary>Applies the changed values to <paramref name="sourcePath"/> and writes the result to <paramref name="targetPath"/>.</summary>
    public static async Task SaveAsync(AppConfig cfg, string sourcePath, string targetPath, IReadOnlyDictionary<string, string> changes)
    {
        var args = new List<string> { "settings-write", sourcePath, targetPath };
        args.AddRange(changes.Select(c => $"{c.Key}={c.Value}"));
        var output = await RunHelperAsync(cfg, [.. args]);
        if (!output.Contains("OK"))
            throw new InvalidOperationException(Txt.L("Speichern fehlgeschlagen:\n", "Saving failed:\n") + output);
    }

    static async Task<string> RunHelperAsync(AppConfig cfg, params string[] args)
    {
        var helperJar = Path.Combine(AppContext.BaseDirectory, "RandoHelper.jar");
        if (!File.Exists(helperJar)) throw new FileNotFoundException(Txt.L("RandoHelper.jar fehlt neben der App.", "RandoHelper.jar is missing next to the app."), helperJar);
        if (!File.Exists(cfg.RandomizerJar)) throw new FileNotFoundException(Txt.L("PokeRandoZX.jar nicht gefunden.", "PokeRandoZX.jar not found."), cfg.RandomizerJar);

        var psi = new ProcessStartInfo(cfg.ResolveJava())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-cp");
        psi.ArgumentList.Add($"{cfg.RandomizerJar}{Path.PathSeparator}{helperJar}");
        psi.ArgumentList.Add("RandoHelper");
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException((await stderr).Trim() is { Length: > 0 } err ? err : Txt.L($"Java-Helfer beendet mit Code {proc.ExitCode}", $"Java helper ended with code {proc.ExitCode}"));
        return await stdout;
    }
}
