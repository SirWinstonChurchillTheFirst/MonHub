using System.Text.RegularExpressions;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;

namespace MonHub;

/// <summary>One player's encounter on a route: the Pokémon (0 = none yet) and whether it is gone (died / not caught).</summary>
public class NuzlockeCatch
{
    public int Species { get; set; }
    public bool Gone { get; set; }
}

public class NuzlockeRoute
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Added by the player (hacks, places the list doesn't know).</summary>
    public bool Custom { get; set; }
    /// <summary>One entry per player, same order as <see cref="NuzlockeRun.Players"/>.</summary>
    public List<NuzlockeCatch> Catches { get; set; } = new();
}

/// <summary>The tracker of one ROM, saved as System\Einstellungen\Nuzlocke\&lt;ROM name&gt;.json.</summary>
public class NuzlockeRun
{
    public string Version { get; set; } = "";
    public List<string> Players { get; set; } = [FirstPlayer];

    public static string FirstPlayer => Txt.L("Spieler 1", "Player 1");
    /// <summary>Soul Link: the players' Pokémon of a route are linked – struck (or not) together, per route.</summary>
    public bool SoulLink { get; set; }
    public List<NuzlockeRoute> Routes { get; set; } = new();

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string PathFor(string rom) => System.IO.Path.Combine(HubPaths.Nuzlocke, System.IO.Path.GetFileNameWithoutExtension(rom) + ".json");

    /// <summary>The saved tracker, completed with the game's route list (in story order); a new one if there is none.</summary>
    public static NuzlockeRun Load(GameEntry game)
    {
        NuzlockeRun? run = null;
        var path = PathFor(game.Rom);
        try
        {
            if (File.Exists(path)) run = JsonSerializer.Deserialize<NuzlockeRun>(File.ReadAllText(path), Options);
        }
        catch
        {
            // broken file: start fresh rather than crash – but keep it, the fresh tracker is saved over it on the next change
            try { File.Copy(path, path + ".kaputt", overwrite: true); } catch { }
        }
        run ??= new NuzlockeRun();
        // a hand-edited or old file may have empty spots ("Players": null …) – the page expects complete lists
        // "Spieler 2" / "Player 2" are MonHub's own names – they follow the language; names the player typed stay
        run.Players = run.Players?.Select(p => p ?? "")
            .Select(p => Regex.Match(p, @"^(Spieler|Player) (\d+)$") is { Success: true } m ? Txt.L($"Spieler {m.Groups[2].Value}", $"Player {m.Groups[2].Value}") : p)
            .ToList() ?? [];
        run.Routes = (run.Routes ?? []).Where(r => r != null).ToList();
        foreach (var route in run.Routes)
        {
            route.Id ??= "";
            route.Name ??= "";
            route.Catches = (route.Catches ?? []).Select(c => c ?? new NuzlockeCatch()).ToList();
        }
        run.Version ??= "";
        if (run.Players.Count == 0) run.Players.Add(NuzlockeRun.FirstPlayer);
        run.Version = NuzlockeData.VersionOf(game) ?? run.Version;

        // the game's places in story order, the player's own ones where they were
        var known = run.Routes.DistinctBy(r => r.Id).ToDictionary(r => r.Id); // a doubled ID would throw
        var merged = new List<NuzlockeRoute>();
        foreach (var (id, name) in NuzlockeData.RoutesFor(run.Version))
        {
            // a known place takes its name from the list – in the language MonHub speaks now
            if (known.Remove(id, out var saved)) saved.Name = name;
            merged.Add(saved ?? new NuzlockeRoute { Id = id, Name = name });
        }
        merged.AddRange(known.Values); // custom routes (and places of another list) at the end
        run.Routes = merged;
        foreach (var route in run.Routes)
            while (route.Catches.Count < run.Players.Count) route.Catches.Add(new NuzlockeCatch());
        return run;
    }

    public void Save(GameEntry game)
    {
        Directory.CreateDirectory(HubPaths.Nuzlocke);
        SafeFile.WriteAllText(PathFor(game.Rom), JsonSerializer.Serialize(this, Options));
    }
}

/// <summary>Route lists per game (Assets\Nuzlocke\routes.json, built by tools/build_nuzlocke.py) and the game → list match.</summary>
public static class NuzlockeData
{
    static Dictionary<string, List<RouteEntry>>? _routes;

    record RouteEntry(string id, string name, string? en);

    public static IEnumerable<(string Id, string Name)> RoutesFor(string version)
    {
        if (_routes == null)
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Nuzlocke/routes.json"))!.Stream;
            _routes = JsonSerializer.Deserialize<Dictionary<string, List<RouteEntry>>>(stream) ?? new();
        }
        return _routes.TryGetValue(version, out var list) ? list.Select(r => (r.id, Txt.English ? r.en ?? r.name : r.name)) : [("starter", "Starter")];
    }

    static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ADA"] = "diamond", ["APA"] = "pearl", ["CPU"] = "platinum", ["IPK"] = "heartgold", ["IPG"] = "soulsilver",
        ["IRB"] = "black", ["IRA"] = "white", ["IRE"] = "black-2", ["IRD"] = "white-2",
        ["AXV"] = "ruby", ["AXP"] = "sapphire", ["BPE"] = "emerald", ["BPR"] = "firered", ["BPG"] = "leafgreen",
        // 3DS (product code CTR-P-EKJA …)
        ["EKJ"] = "x", ["EK2"] = "y", ["ECR"] = "omega-ruby", ["ECL"] = "alpha-sapphire",
        ["BND"] = "sun", ["BNE"] = "moon", ["A2A"] = "ultra-sun", ["A2B"] = "ultra-moon",
    };

    static readonly (string[] Words, string Version)[] ByTitle =
    [
        (["CRYSTAL", "KRISTALL"], "crystal"), (["GOLD", "GLD"], "gold"), (["SILVER", "SILBER", "SLV"], "silver"),
        (["YELLOW", "GELB", "YEL"], "yellow"), (["BLUE", "BLAU", "BLU"], "blue"), (["RED", "ROT"], "red"),
    ];

    /// <summary>The game behind a ROM (runs and hacks too), by game code or – Game Boy – header title; null if unknown.</summary>
    public static string? VersionOf(GameEntry game)
    {
        if (game.GameCode.Length >= 3 && ByCode.TryGetValue(game.GameCode[..3], out var version)) return version;
        if (game.System != GameSystem.GB) return null;
        var title = game.HeaderTitle.ToUpperInvariant();
        return ByTitle.FirstOrDefault(t => t.Words.Any(title.Contains)).Version;
    }
}

/// <summary>A Pokémon in the pickers: "#025 Pikachu".</summary>
public record SpeciesChoice(int Dex, string Name)
{
    public override string ToString() => Name;

    public static readonly List<SpeciesChoice> All =
        [new(0, "–"), .. Enumerable.Range(1, 809).Select(d => new SpeciesChoice(d, Species.Name(d))).OrderBy(s => s.Name, StringComparer.CurrentCulture)];
}

/// <summary>One cell of the tracker (a player on a route), live-updating sprite and strike.</summary>
public class CatchView(NuzlockeCatch data, Action changed, bool soulLink) : INotifyPropertyChanged
{
    public NuzlockeCatch Data { get; } = data;

    /// <summary>With Soul Link the strike is per route, not per player.</summary>
    public Visibility ToggleVisibility => soulLink ? Visibility.Collapsed : Visibility.Visible;

    public SpeciesChoice? Choice
    {
        get => SpeciesChoice.All.FirstOrDefault(s => s.Dex == Data.Species);
        set
        {
            Data.Species = value?.Dex ?? 0;
            Changed();
        }
    }

    public bool Gone
    {
        get => Data.Gone;
        set
        {
            Data.Gone = value;
            Changed();
        }
    }

    public string? Sprite => Data.Species > 0 ? Species.SpriteKey(Data.Species) : null;
    public double Fade => Data.Gone ? 0.35 : 1;
    public TextDecorationCollection? Strike => Data.Gone ? TextDecorations.Strikethrough : null;

    void Changed([CallerMemberName] string? _ = null)
    {
        foreach (var name in new[] { nameof(Choice), nameof(Gone), nameof(Sprite), nameof(Fade), nameof(Strike) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        changed();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class RouteView : INotifyPropertyChanged
{
    public RouteView(NuzlockeRoute route, List<CatchView> cells, bool soulLink)
    {
        Route = route;
        Cells = cells;
        RouteToggleVisibility = soulLink ? Visibility.Visible : Visibility.Collapsed;
        foreach (var cell in cells)
            cell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(CatchView.Gone)) return;
                foreach (var name in new[] { nameof(Gone), nameof(Fade), nameof(Strike) })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            };
    }

    public NuzlockeRoute Route { get; }
    public List<CatchView> Cells { get; }
    public string Name => Route.Name;
    public Visibility RemoveVisibility => Route.Custom ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RouteToggleVisibility { get; }
    public bool Open => Cells.Any(c => c.Data.Species == 0 && !c.Data.Gone);

    /// <summary>The whole route struck (Soul Link: one button for all players' linked Pokémon).</summary>
    public bool Gone
    {
        get => Cells.Count > 0 && Cells.All(c => c.Data.Gone);
        set
        {
            foreach (var cell in Cells) cell.Gone = value;
        }
    }

    public double Fade => Gone ? 0.45 : 1;
    public TextDecorationCollection? Strike => Gone ? TextDecorations.Strikethrough : null;

    public event PropertyChangedEventHandler? PropertyChanged;
}
