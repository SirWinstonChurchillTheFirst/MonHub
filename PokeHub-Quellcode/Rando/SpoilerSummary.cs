using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RandoApp;

/// <summary>Pulls the interesting bits (starters, static Pokémon) out of the randomizer's full spoiler log.</summary>
public static class SpoilerSummary
{
    /// <summary>Original starters by the first 3 letters of the NDS game code, in the language of the ROM.</summary>
    static string[]? OriginalStarters(string gameCode)
    {
        if (gameCode.Length < 4) return null;
        bool german = gameCode[3] == 'D';
        return gameCode[..3] switch
        {
            "IRB" or "IRA" or "IRE" or "IRD" => german ? ["Serpifeu", "Floink", "Ottaro"] : ["Snivy", "Tepig", "Oshawott"],
            "ADA" or "APA" or "CPU" => german ? ["Chelast", "Panflam", "Plinfa"] : ["Turtwig", "Chimchar", "Piplup"],
            "IPK" or "IPG" => german ? ["Endivie", "Feurigel", "Karnimani"] : ["Chikorita", "Cyndaquil", "Totodile"],
            // 3DS: one ROM for all languages, the randomizer's log uses the English names
            "EKJ" or "EK2" => ["Chespin", "Fennekin", "Froakie", "Bulbasaur", "Charmander", "Squirtle"],
            "ECR" or "ECL" => ["Treecko", "Torchic", "Mudkip"],
            "BND" or "BNE" or "A2A" or "A2B" => ["Rowlet", "Litten", "Popplio"],
            _ => null,
        };
    }

    /// <param name="gameCode">The game code when the ROM header has none (3DS: read from the game's own files).</param>
    public static string Build(string logPath, RomInfo rom, string? gameCode = null)
    {
        var lines = File.ReadAllLines(logPath);
        var sb = new StringBuilder();

        // Starters: "Set starter 1 to Enton"
        var starters = lines.Select(l => Regex.Match(l, @"^Set starter (\d+) to (.+)$"))
                            .Where(m => m.Success)
                            .Select(m => (Index: int.Parse(m.Groups[1].Value), Name: m.Groups[2].Value.Trim()))
                            .ToList();
        var originals = OriginalStarters(rom.GameCode.Length > 0 ? rom.GameCode : gameCode ?? "");
        sb.AppendLine("STARTER");
        if (starters.Count == 0)
            sb.AppendLine("  unverändert");
        foreach (var (index, name) in starters)
        {
            var from = originals != null && index <= originals.Length ? originals[index - 1] : $"Starter {index}";
            sb.AppendLine($"  {from,-22} → {name}");
        }

        // Static Pokémon: section "--Static Pokemon--" with "Old => New" lines until the next blank line.
        sb.AppendLine();
        int start = Array.FindIndex(lines, l => l.Trim() == "--Static Pokemon--");
        var statics = new List<(string From, string To)>();
        if (start >= 0)
        {
            for (int i = start + 1; i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]); i++)
            {
                var parts = lines[i].Split(" => ", 2);
                if (parts.Length == 2) statics.Add((parts[0].Trim(), parts[1].Trim()));
            }
        }
        sb.AppendLine(statics.Count == 0 ? "STATISCHE POKÉMON" : $"STATISCHE POKÉMON ({statics.Count})");
        if (statics.Count == 0)
            sb.AppendLine("  unverändert");
        foreach (var (from, to) in statics)
            sb.AppendLine($"  {from,-22} → {to}");

        return sb.ToString();
    }
}
