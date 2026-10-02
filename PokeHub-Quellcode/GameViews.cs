using System.Windows;
using System.Windows.Media;

namespace PokeHub;

/// <summary>One party member as shown in a team row.</summary>
public class TeamSlot(PartyMember member)
{
    public PartyMember Member { get; } = member;
    public string? Sprite => Member.IsEgg ? null : Species.SpriteKey(Member.Species);
    public string LevelText => Member.IsEgg ? "Ei" : $"Lv.{Member.Level}";
    public string Tooltip
    {
        get
        {
            if (Member.IsEgg) return "Ein Ei";
            var name = Species.Name(Member.Species);
            bool nicknamed = Member.Nickname.Length > 0 && !Member.Nickname.Equals(name, StringComparison.OrdinalIgnoreCase);
            return nicknamed ? $"{Member.Nickname} ({name}) · Lv. {Member.Level}" : $"{name} · Lv. {Member.Level}";
        }
    }
    public Visibility EggVisibility => Member.IsEgg ? Visibility.Visible : Visibility.Collapsed;

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
        AccentGlow = Frozen(new LinearGradientBrush(Color.FromArgb(0x70, a.R, a.G, a.B), Color.FromArgb(0x10, a.R, a.G, a.B), new Point(0, 0), new Point(1, 1)));
        AccentSolid = Frozen(new SolidColorBrush(a));
        AccentSoft = Frozen(new SolidColorBrush(Color.FromArgb(0x33, a.R, a.G, a.B)));
        // text on the version colour: dark on light colours (Weiß, Gelb), white otherwise
        AccentInk = (0.299 * a.R + 0.587 * a.G + 0.114 * a.B) / 255 > 0.62 ? Brushes.Black : Brushes.White;
    }

    static readonly Brush GameBoyLight = Frozen(new SolidColorBrush(Color.FromRgb(0x8B, 0xAC, 0x0F)));
    static readonly Brush GameBoyLightest = Frozen(new SolidColorBrush(Color.FromRgb(0x9B, 0xBC, 0x0F)));
    static readonly Brush GameBoyDark = Frozen(new SolidColorBrush(Color.FromRgb(0x30, 0x62, 0x30)));

    public GameEntry Game { get; }
    public GameLook Look { get; }
    public SaveSummary? Summary => Game.Summary;

    public string Name => Game.Name;
    public string Sprite => Look.Sprite;
    public string Version => GameLook.Region(Game.GameCode) is { Length: > 0 } region ? $"{Look.Version} · {region}" : Look.Version;
    public Visibility VersionVisibility => Look.Version.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string KindText => Game.IsRun ? "Run" : "Original";
    public string SystemText => Game.SystemText;
    public string SaveTime => Game.NewestSave is { } save ? TimeText.Ago(save.Time) : "Kein Spielstand";
    public string SaveEmulator => Game.NewestSave?.Emulator ?? "";
    public bool HasSave => Game.NewestSave != null;
    public Visibility SaveVisibility => Game.IsDS || Game.Is3DS || Game.IsGameBoy ? Visibility.Visible : Visibility.Collapsed;
    public bool IsFavorite => HubConfig.Current.IsFavorite(Game.Rom);
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    /// <summary>"Antje · 8:45 · 1 Orden" – what the game shows on its continue screen.</summary>
    public string TrainerLine => Summary is { } s ? s.BadgeSlots > 0 ? $"{s.Trainer} · {s.PlayTime} · {s.Badges} Orden" : $"{s.Trainer} · {s.PlayTime}" : "";
    public Visibility TrainerVisibility => Summary != null ? Visibility.Visible : Visibility.Collapsed;
    public List<TeamSlot> Team => _team ??= TeamSlot.Of(Summary);
    List<TeamSlot>? _team;

    public Brush AccentGlow { get; }
    public Brush AccentSolid { get; }
    public Brush AccentSoft { get; }
    public Brush AccentInk { get; }

    static T Frozen<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }
}
