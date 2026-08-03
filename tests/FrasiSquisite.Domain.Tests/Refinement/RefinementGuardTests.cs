using FrasiSquisite.Domain.Refinement;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Refinement;

public class RefinementGuardTests
{
    private const string Template =
        "{0} {1} {2} {3}, {4}, dicendo: «{5}». La gente dice: «{6}», ed è andata a finire che {7}.";

    private const string Semplice = "{0} {1}";

    [Fact]
    public void UnaRifinituraValidaVieneAccettata()
    {
        var esito = RefinementGuard.Applica(["la nonna", "la mamma"], ["la nonna", "con la mamma"], Semplice);

        Assert.Equal(["la nonna", "con la mamma"], esito);
    }

    [Fact]
    public void UnaCasellaLasciataIdenticaVaBene()
    {
        var esito = RefinementGuard.Applica(["balla", "male"], ["balla", "è finita male"], Semplice);

        Assert.Equal(["balla", "è finita male"], esito);
    }

    /// <summary>
    /// Il controllo che protegge il gioco: il divertimento vive degli
    /// incidenti dei giocatori, e un modello che trasforma "il cadavere
    /// squisito" in "il defunto elegante" lo ucciderebbe. La casella
    /// riscritta torna grezza; le altre passano lo stesso.
    /// </summary>
    [Fact]
    public void UnaCasellaRiscrittaTornaGrezzaSenzaTrascinareLeAltre()
    {
        var esito = RefinementGuard.Applica(
            ["il cadavere squisito", "la mamma"],
            ["il defunto elegante", "con la mamma"],
            Semplice);

        Assert.Equal(["il cadavere squisito", "con la mamma"], esito);
    }

    [Fact]
    public void IlContenimentoIgnoraMaiuscoleESpaziDoppi()
    {
        var esito = RefinementGuard.Applica(["la  nonna"], ["Con La Nonna"], "{0}");

        Assert.Equal(["Con La Nonna"], esito);
    }

    /// <summary>
    /// Il template mette gia' "ed è andata a finire che" davanti alla casella
    /// 7: se il modello lo ripete, la frase composta diventa "ed è andata a
    /// finire che ed è andata a finire che male".
    /// </summary>
    [Fact]
    public void UnaCasellaCheRipeteIlLetteraleDelTemplateTornaGrezza()
    {
        var grezze = new[] { "a", "b", "c", "d", "e", "f", "g", "male" };
        var rifinite = new[] { "a", "b", "c", "d", "e", "f", "g", "ed è andata a finire che è finita male" };

        var esito = RefinementGuard.Applica(grezze, rifinite, Template);

        Assert.Equal("male", esito[7]);
        Assert.Equal("a", esito[0]);
    }

    [Fact]
    public void UnNumeroDiCaselleDiversoScartaTuttaLaFrase()
    {
        var esito = RefinementGuard.Applica(["uno", "due"], ["uno"], Semplice);

        Assert.Equal(["uno", "due"], esito);
    }

    [Fact]
    public void SenzaRifinituraSiTengonoLeGrezze()
    {
        var esito = RefinementGuard.Applica(["uno", "due"], null, Semplice);

        Assert.Equal(["uno", "due"], esito);
    }

    /// <summary>
    /// Il limite di 60 caratteri della validazione vale per l'input umano e
    /// non qui, perche' rifinire per definizione allunga. Resta un tetto piu'
    /// largo perche' il modello non possa restituire un paragrafo.
    /// </summary>
    [Fact]
    public void UnaCasellaSmisurataTornaGrezza()
    {
        var lunga = "male " + new string('x', RefinementGuard.MaxCaratteri);

        var esito = RefinementGuard.Applica(["male"], [lunga], "{0}");

        Assert.Equal(["male"], esito);
    }

    [Fact]
    public void UnaCasellaRifinitaVuotaTornaGrezza()
    {
        var esito = RefinementGuard.Applica(["male"], ["   "], "{0}");

        Assert.Equal(["male"], esito);
    }
}
