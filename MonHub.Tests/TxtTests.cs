using MonHub;

namespace MonHub.Tests;

/// <summary>The two languages: the right text is picked, and the language-dependent data follows.</summary>
[Collection("Language")] // Txt's language is process-wide – these tests must not run in parallel with each other
public class TxtTests
{
    [Fact]
    public void English_PicksTheEnglishText()
    {
        Txt.Use(english: true);
        Assert.Equal("Delete game", Txt.L("Spiel löschen", "Delete game"));
        Assert.Equal("en", Txt.Code);
    }

    [Fact]
    public void German_PicksTheGermanText()
    {
        try
        {
            Txt.Use(english: false);
            Assert.Equal("Spiel löschen", Txt.L("Spiel löschen", "Delete game"));
            Assert.Equal("de", Txt.Code);
        }
        finally
        {
            Txt.Use(english: true);
        }
    }

    [Fact]
    public void SpeciesNames_FollowTheLanguage()
    {
        try
        {
            Txt.Use(english: true);
            Assert.Equal("Eevee", Species.Name(133));
            Assert.Equal("Charizard", Species.Name(6));
            Txt.Use(english: false);
            Assert.Equal("Evoli", Species.Name(133));
            Assert.Equal("Glurak", Species.Name(6));
        }
        finally
        {
            Txt.Use(english: true);
        }
    }

    [Fact]
    public void TimeText_FollowsTheLanguage()
    {
        try
        {
            var twoHours = DateTime.Now.AddHours(-2.5);
            Txt.Use(english: true);
            Assert.Equal("2 h ago", TimeText.Ago(twoHours));
            Txt.Use(english: false);
            Assert.Equal("vor 2 Std.", TimeText.Ago(twoHours));
        }
        finally
        {
            Txt.Use(english: true);
        }
    }

    [Fact]
    public void LanguageExtension_ReturnsTheTextOfTheLanguage()
    {
        try
        {
            var ext = new LExtension("Weiter", "Continue");
            Txt.Use(english: true);
            Assert.Equal("Continue", ext.ProvideValue(null!));
            Txt.Use(english: false);
            Assert.Equal("Weiter", ext.ProvideValue(null!));
        }
        finally
        {
            Txt.Use(english: true);
        }
    }
}
