using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MonHub;

/// <summary>
/// How a game looks in MonHub: its version, box Pokémon and version colour – found by the game code in the ROM header,
/// so runs and hacks with any file name still show the right game.
/// </summary>
public record GameLook(string Version, string Sprite, Color Accent)
{
    static Color Rgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    // DS (first 3 letters of the game code) and GBA games
    static readonly Dictionary<string, GameLook> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ADA"] = new(Txt.L("Diamant", "Diamond"), "0483/idle", Rgb(0x5B8DEF)),
        ["APA"] = new(Txt.L("Perl", "Pearl"), "0484/idle", Rgb(0xE58BB0)),
        ["CPU"] = new(Txt.L("Platin", "Platinum"), "0487/idle", Rgb(0x9C8AD0)),
        ["IPK"] = new("HeartGold", "0250/idle", Rgb(0xE9B949)),
        ["IPG"] = new("SoulSilver", "0249/idle", Rgb(0xB8C4D6)),
        ["IRB"] = new(Txt.L("Schwarz", "Black"), "0643/idle", Rgb(0x8A8F98)),
        ["IRA"] = new(Txt.L("Weiß", "White"), "0644/idle", Rgb(0xE8E8E8)),
        ["IRE"] = new(Txt.L("Schwarz 2", "Black 2"), "0646/idle", Rgb(0x5C7090)),
        ["IRD"] = new(Txt.L("Weiß 2", "White 2"), "0646/idle", Rgb(0xDCE6F2)),
        ["AXV"] = new(Txt.L("Rubin", "Ruby"), "0383/idle", Rgb(0xD32F4A)),
        ["AXP"] = new(Txt.L("Saphir", "Sapphire"), "0382/idle", Rgb(0x2F5FD3)),
        ["BPE"] = new(Txt.L("Smaragd", "Emerald"), "0384/idle", Rgb(0x2FAF6E)),
        ["BPR"] = new(Txt.L("Feuerrot", "FireRed"), "0006/idle", Rgb(0xF26B21)),
        ["BPG"] = new(Txt.L("Blattgrün", "LeafGreen"), "0003/idle", Rgb(0x5DBB4A)),
        // 3DS: first 3 letters of the product code (CTR-P-EKJA)
        ["EKJ"] = new("X", "species/0716", Rgb(0x2E6BD6)),
        ["EK2"] = new("Y", "species/0717", Rgb(0xD6303E)),
        ["ECR"] = new(Txt.L("Omega Rubin", "Omega Ruby"), "species/0383", Rgb(0xC62839)),
        ["ECL"] = new(Txt.L("Alpha Saphir", "Alpha Sapphire"), "species/0382", Rgb(0x2F66C9)),
        ["BND"] = new(Txt.L("Sonne", "Sun"), "species/0791", Rgb(0xF08A24)),
        ["BNE"] = new(Txt.L("Mond", "Moon"), "species/0792", Rgb(0x6A5ACD)),
        ["A2A"] = new(Txt.L("Ultrasonne", "Ultra Sun"), "species/0800", Rgb(0xF2A33A)),
        ["A2B"] = new(Txt.L("Ultramond", "Ultra Moon"), "species/0800", Rgb(0x4B6FD8)),
    };

    // Game Boy games have no game code – their header title says it (German and English)
    static readonly (string[] Words, GameLook Look)[] ByTitle =
    [
        (["CRYSTAL", "KRISTALL"], new(Txt.L("Kristall", "Crystal"), "0245/idle", Rgb(0x5DC8E0))),
        (["GOLD", "GLD"], new("Gold", "0250/idle", Rgb(0xD4A017))),
        (["SILVER", "SILBER", "SLV"], new(Txt.L("Silber", "Silver"), "0249/idle", Rgb(0xA9B4C2))),
        (["YELLOW", "GELB"], new(Txt.L("Gelb", "Yellow"), "0025/idle", Rgb(0xF7C948))),
        (["BLUE", "BLAU"], new(Txt.L("Blau", "Blue"), "0009/idle", Rgb(0x3B6FD8))),
        (["RED", "ROT"], new(Txt.L("Rot", "Red"), "0006/idle", Rgb(0xE3350D))),
    ];

    static readonly GameLook Unknown = new("", "0025/idle", Rgb(0x7C8BA6));

    public static GameLook For(GameEntry game)
    {
        if (game.GameCode.Length >= 3 && ByCode.TryGetValue(game.GameCode[..3], out var look)) return look;
        if (game.System == GameSystem.GB)
        {
            var title = game.HeaderTitle.ToUpperInvariant();
            foreach (var (words, gbLook) in ByTitle)
                if (words.Any(title.Contains)) return gbLook;
        }
        return Unknown;
    }

    /// <summary>Language of the game code's last letter ("CPUD" = German), "" when unknown.</summary>
    public static string Region(string gameCode) => gameCode.Length < 4 ? "" : char.ToUpperInvariant(gameCode[3]) switch
    {
        'D' => "DE",
        'E' or 'O' or 'P' => "EN",
        'F' => "FR",
        'I' => "IT",
        'S' => "ES",
        'J' => "JP",
        'K' => "KR",
        _ => "",
    };
}

/// <summary>
/// An image that plays a PMD sprite animation (key like "0487/idle"). Animation rules (pause when hidden, window in the
/// background, dialog open, animations off) come from <see cref="RandoApp.SpriteAnimator"/>.
/// </summary>
public class SpriteView : Image
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(string), typeof(SpriteView),
        new PropertyMetadata(null, (d, e) => RandoApp.SpriteAnimator.For((SpriteView)d).Key = (string?)e.NewValue));

    /// <summary>True: stays on its first frame (e.g. until the card is hovered).</summary>
    public static readonly DependencyProperty StillProperty = DependencyProperty.Register(
        nameof(Still), typeof(bool), typeof(SpriteView), new PropertyMetadata(false, (d, _) => ((SpriteView)d).UpdateHold()));

    public string? Key
    {
        get => (string?)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public bool Still
    {
        get => (bool)GetValue(StillProperty);
        set => SetValue(StillProperty, value);
    }

    /// <summary>True: a detail (team member) – only moves when motion is at the full level, not at "calm".</summary>
    public static readonly DependencyProperty DecorativeProperty = DependencyProperty.Register(
        nameof(Decorative), typeof(bool), typeof(SpriteView), new PropertyMetadata(false, (d, _) => ((SpriteView)d).UpdateHold()));

    public bool Decorative
    {
        get => (bool)GetValue(DecorativeProperty);
        set => SetValue(DecorativeProperty, value);
    }

    public SpriteView()
    {
        RandoApp.SpriteAnimator.For(this);
        Loaded += (_, _) => { HubMotion.Changed += UpdateHold; UpdateHold(); };
        Unloaded += (_, _) => HubMotion.Changed -= UpdateHold;
    }

    void UpdateHold() => RandoApp.SpriteAnimator.For(this).Hold = Still || (Decorative && !HubMotion.Decorations);
}
