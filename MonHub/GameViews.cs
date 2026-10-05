
using Avalonia.Media.Immutable;

namespace MonHub;

/// <summary>One party member as shown in a team row.</summary>
public class TeamSlot(PartyMember member)
{
    public PartyMember Member { get; } = member;
    public string? Sprite => Member.IsEgg ? null : Species.SpriteKey(Member.Species);
    public string LevelText => Member.IsEgg ? Txt.L("Ei", "Egg") : $"Lv.{Member.Level}";
    public string Tooltip
    {
        get
        {
            if (Member.IsEgg) return Txt.L("Ein Ei", "An egg");
            var name = Species.Name(Member.Species);
            bool nicknamed = Member.Nickname.Length > 0 && !Member.Nickname.Equals(name, StringComparison.OrdinalIgnoreCase);
            return nicknamed ? $"{Member.Nickname} ({name}) · Lv. {Member.Level}" : $"{name} · Lv. {Member.Level}";
        }
    }
    public bool IsEgg => Member.IsEgg;

    public static List<TeamSlot> Of(SaveSummary? summary) => summary?.Party.Select(p => new TeamSlot(p)).ToList() ?? [];
}

/// <summary>A game in the lists: the data plus how it looks (box Pokémon, version colour, save info).</summary>
public class GameCard
{
    public GameCard(GameEntry game)
    {
        Game = game;
        Look = GameLook.For(game);
        if (RandoApp.Sprites.Tint == RandoApp.SpriteTint.GameBoy)
        {
            // the Game Boy screen has four greens and nothing else – version colours too
            AccentGlow = AccentSoft = GameBoyLight;
            AccentSolid = GameBoyDark;
            AccentInk = GameBoyLightest;
            return;
        }
        var a = Look.Accent;
        // immutable brushes: cards are also built off the UI thread (the new-run list)
        AccentGlow = new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, Color.FromArgb(0x70, a.R, a.G, a.B)), new ImmutableGradientStop(1, Color.FromArgb(0x10, a.R, a.G, a.B))],
            startPoint: RelativePoint.TopLeft, endPoint: RelativePoint.BottomRight);
        AccentSolid = new ImmutableSolidColorBrush(a);
        AccentSoft = new ImmutableSolidColorBrush(Color.FromArgb(0x33, a.R, a.G, a.B));
        // text on the version colour: dark on light colours (Weiß, Gelb), white otherwise
        AccentInk = (0.299 * a.R + 0.587 * a.G + 0.114 * a.B) / 255 > 0.62 ? Brushes.Black : Brushes.White;
    }

    static readonly Brush GameBoyLight = new ImmutableSolidColorBrush(Color.FromRgb(0x8B, 0xAC, 0x0F));
    static readonly Brush GameBoyLightest = new ImmutableSolidColorBrush(Color.FromRgb(0x9B, 0xBC, 0x0F));
    static readonly Brush GameBoyDark = new ImmutableSolidColorBrush(Color.FromRgb(0x30, 0x62, 0x30));

    public GameEntry Game { get; }
    public GameLook Look { get; }
    public SaveSummary? Summary => Game.Summary;

    public string Name => Game.Name;
    public string Sprite => Look.Sprite;
    public string Version => GameLook.Region(Game.GameCode) is { Length: > 0 } region ? $"{Look.Version} · {region}" : Look.Version;
    public bool VersionVisibility => Look.Version.Length > 0;
    public string KindText => Game.IsRun ? "Run" : "Original";
    public string SystemText => Game.SystemText;
    public string SaveTime => Game.NewestSave is { } save ? TimeText.Ago(save.Time) : Txt.L("Kein Spielstand", "No save");
    public string SaveEmulator => Game.NewestSave?.Emulator ?? "";
    public bool HasSave => Game.NewestSave != null;
    public bool SaveVisibility => Game.IsDS || Game.Is3DS || Game.IsGameBoy;
    public bool IsFavorite => HubConfig.Current.IsFavorite(Game.Rom);
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    /// <summary>"Antje · 8:45 · 1 Orden" – what the game shows on its continue screen.</summary>
    public string TrainerLine => Summary is { } s ? s.BadgeSlots > 0 ? $"{s.Trainer} · {s.PlayTime} · {s.Badges} " + Txt.L("Orden", s.Badges == 1 ? "badge" : "badges") : $"{s.Trainer} · {s.PlayTime}" : "";
    public bool TrainerVisibility => Summary != null;
    public List<TeamSlot> Team => _team ??= TeamSlot.Of(Summary);
    List<TeamSlot>? _team;

    public Brush AccentGlow { get; }
    public Brush AccentSolid { get; }
    public Brush AccentSoft { get; }
    public Brush AccentInk { get; }
}
