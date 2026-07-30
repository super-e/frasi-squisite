using FrasiSquisite.App.Services;
using Xunit;

namespace FrasiSquisite.App.Tests.Services;

public class ThemeServiceTests
{
    [Fact]
    public void SenzaUnaSceltaSalvataPartePartendoDaSurrealistaPop()
    {
        var servizio = new ThemeService(new FakeThemeStore());

        Assert.Equal(ThemeChoice.SurrealistaPop, servizio.Current);
    }

    [Fact]
    public void SetThemeAggiornaCurrentELoPersiste()
    {
        var store = new FakeThemeStore();
        var servizio = new ThemeService(store);

        servizio.SetTheme(ThemeChoice.NotteDiGioco);

        Assert.Equal(ThemeChoice.NotteDiGioco, servizio.Current);
        Assert.Equal(ThemeChoice.NotteDiGioco, store.Load());
    }

    // La scelta va riapplicata all'avvio (lotto-a-brief.md): un nuovo
    // ThemeService costruito sullo stesso store deve ripartire da dove
    // l'utente l'aveva lasciata, non dal default.
    [Fact]
    public void UnNuovoServizioSulloStessoStoreRileggeLaSceltaPrecedente()
    {
        var store = new FakeThemeStore();
        new ThemeService(store).SetTheme(ThemeChoice.NotteDiGioco);

        var riletto = new ThemeService(store);

        Assert.Equal(ThemeChoice.NotteDiGioco, riletto.Current);
    }

    [Fact]
    public void SetThemeSolleveThemeChanged()
    {
        var servizio = new ThemeService(new FakeThemeStore());
        ThemeChoice? ricevuto = null;
        servizio.ThemeChanged += tema => ricevuto = tema;

        servizio.SetTheme(ThemeChoice.NotteDiGioco);

        Assert.Equal(ThemeChoice.NotteDiGioco, ricevuto);
    }

    [Fact]
    public void ApplyInitialSolleveThemeChangedConIlValoreLetto()
    {
        var store = new FakeThemeStore();
        store.Save(ThemeChoice.NotteDiGioco);
        var servizio = new ThemeService(store);
        ThemeChoice? ricevuto = null;
        servizio.ThemeChanged += tema => ricevuto = tema;

        servizio.ApplyInitial();

        Assert.Equal(ThemeChoice.NotteDiGioco, ricevuto);
    }
}
