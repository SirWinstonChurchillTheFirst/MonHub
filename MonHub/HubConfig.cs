using System.IO;
using System.Text.Json;

namespace MonHub;

public class Fangame
{
    public string Name { get; set; } = "";
    /// <summary>Program (.exe) or any file (e.g. a .gba hack), opened with its default program.</summary>
    public string Path { get; set; } = "";
}

/// <summary>How much moves on screen.</summary>
public enum MotionLevel
{
    /// <summary>Everything: Pokémon, theme details (still paused while MonHub is in the background).</summary>
    All,
    /// <summary>Only the big Pokémon of the selected game – no theme animations, teams stand still.</summary>
    Calm,
    /// <summary>Nothing moves.</summary>
    Off,
}

/// <summary>Launcher settings (System\Einstellungen\MonHub.json).</summary>
public class HubConfig
{
    public List<Fangame> Fangames { get; set; } = new();

    /// <summary>Theme id (see <see cref="HubThemes"/>).</summary>
    public string Theme { get; set; } = HubThemes.Default;

    /// <summary>null = follow Windows' "animation effects" setting.</summary>
    public MotionLevel? Motion { get; set; }

    /// <summary>"en" or "de", chosen under Optionen; null = not chosen yet (then Setup's language, else English). Read at start by <see cref="Txt"/>.</summary>
    public string? Language { get; set; }

    /// <summary>Trainer name for the start page; empty = the name from the newest save.</summary>
    public string TrainerName { get; set; } = "";

    /// <summary>Keys, controller and speeds for melonDS and mGBA; null = never changed in MonHub (the emulators' own settings stay).</summary>
    public ControlSettings? Controls { get; set; }

    /// <summary>Which of MonHub's Azahar picture settings were applied (once each – later changes in Azahar stay).</summary>
    public int AzaharLook { get; set; }

    /// <summary>The MonHub version whose tour was shown or declined; null = never (new install or before the tour existed).</summary>
    public string? TourSeen { get; set; }

    /// <summary>The installer's stamp (System\App\install.ini) the tour question was answered for – a new stamp = installed again.</summary>
    public string? InstallSeen { get; set; }

    /// <summary>"Vielleicht später": ask again on the next start.</summary>
    public bool TourLater { get; set; }

    /// <summary>
    /// 3DS games with the 60-FPS cheat (they then run twice as fast; R held = 30 FPS). Switch under "Emulatoren".
    /// Off unless chosen: at double speed a short press already counts as held, so menus jump two entries – in a
    /// Yes/No box the cursor lands where it started. (Up to 2.8.0 this was "Azahar60Fps" and on by default; the new
    /// name leaves that old default behind.)
    /// </summary>
    public bool AzaharSixtyFps { get; set; }

    /// <summary>ROM file names (without folder) marked as favourite in the game list.</summary>
    public List<string> Favorites { get; set; } = new();

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The one instance of the running launcher.</summary>
    public static HubConfig Current { get; private set; } = new();

    public static HubConfig Load()
    {
        try
        {
            if (File.Exists(HubPaths.HubConfig))
            {
                Current = JsonSerializer.Deserialize<HubConfig>(File.ReadAllText(HubPaths.HubConfig), Options) ?? new();
                if (Current.Theme == Legacy.RedThemeId) Current.Theme = "red"; // saved with the new id next time
                Current.Controls?.FillMissing();
                return Current;
            }
        }
        catch
        {
            // unreadable config: start fresh instead of crashing – and keep the broken file, the next Save would
            // overwrite it (favourites, fangames, controls)
            try { File.Copy(HubPaths.HubConfig, HubPaths.HubConfig + ".kaputt", overwrite: true); } catch { }
        }
        return Current = new();
    }

    public void Save()
    {
        Directory.CreateDirectory(HubPaths.SettingsDir);
        SafeFile.WriteAllText(HubPaths.HubConfig, JsonSerializer.Serialize(this, Options));
    }

    public MotionLevel EffectiveMotion => Motion ?? (System.Windows.SystemParameters.ClientAreaAnimation ? MotionLevel.All : MotionLevel.Off);

    public bool IsFavorite(string rom) => Favorites.Contains(System.IO.Path.GetFileName(rom), StringComparer.OrdinalIgnoreCase);

    public void ToggleFavorite(string rom)
    {
        var name = System.IO.Path.GetFileName(rom);
        if (Favorites.RemoveAll(f => f.Equals(name, StringComparison.OrdinalIgnoreCase)) == 0) Favorites.Add(name);
        Save();
    }
}
