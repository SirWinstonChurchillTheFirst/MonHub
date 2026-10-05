namespace MonHub;

/// <summary>Every Pokémon up to Gen 7: German and English name and idle sprite (data in SpeciesData.g.cs, built by tools/build_species.py).</summary>
public static partial class Species
{
    public static string Name(int dex) => dex >= 1 && dex <= Names.Length ? Names[dex - 1] : $"#{dex}";

    static string[] Names => Txt.English ? EnglishNames : GermanNames;

    /// <summary>Sprite key for <see cref="SpriteView"/>, null if there is no sprite.</summary>
    public static string? SpriteKey(int dex) => Durations.ContainsKey(dex) ? $"species/{dex:0000}" : null;

    /// <summary>Makes the Pokémon and item sprites available to the shared sprite code (animation, Game Boy colours).</summary>
    public static void Register() => RandoApp.Sprites.MoreSprites = key =>
    {
        if (key.StartsWith("species/", StringComparison.Ordinal) && int.TryParse(key.AsSpan(8), out int dex) && Durations.TryGetValue(dex, out var durations))
            return ($"avares://MonHub/Assets/Species/{dex:0000}.png", durations);
        if (key.StartsWith("portrait/", StringComparison.Ordinal)) // "portrait/0137/Happy": a PMD face
            return ($"avares://MonHub/Assets/Portraits/{key[9..].Replace('/', '-')}.png", [1]);
        if (key.StartsWith("icon/", StringComparison.Ordinal)) // MonHub's own pixel icons (tools/build_icons.py)
            return ($"avares://MonHub/Assets/Icons/{key[5..]}.png", [1]);
        return null;
    };
}
