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
    /// La guardia non verifica più che le parole del giocatore ricompaiano
    /// alla lettera nella casella rifinita (design 2026-08-12 "migliora la
    /// rifinitura", §3.2): per permettere concordanza di genere/numero e
    /// coniugazione, non c'è più un modo puramente sintattico di distinguere
    /// un aggiustamento di forma da una riscrittura completa. La fedeltà
    /// del contenuto resta affidata al prompt, non più al codice - scelta
    /// dell'utente, consapevole del rischio.
    /// </summary>
    [Fact]
    public void UnaCasellaCompletamenteRiscrittaVieneOraAccettata()
    {
        var esito = RefinementGuard.Applica(
            ["il cadavere squisito", "la mamma"],
            ["il defunto elegante", "con la mamma"],
            Semplice);

        Assert.Equal(["il defunto elegante", "con la mamma"], esito);
    }

    /// <summary>
    /// Prova diretta del punto centrale di questo cambiamento (design
    /// 2026-08-12 §3.2): un aggiustamento della forma della parola per
    /// farla concordare (qui, plurale) passa la guardia, cosa impossibile
    /// prima con il controllo di contenimento letterale.
    /// </summary>
    [Fact]
    public void UnaParolaConFormaDiversaPerConcordanzaVieneAccettata()
    {
        var esito = RefinementGuard.Applica(["montagna"], ["su alcune montagne"], "{0}");

        Assert.Equal(["su alcune montagne"], esito);
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

    /// <summary>
    /// Le caselle 5 e 6 del template reale sono precedute da "dicendo: «" e
    /// "La gente dice: «": il letterale finisce con la virgoletta di
    /// apertura, che nessun modello scrive mai davanti al proprio testo.
    /// Senza tagliare anche la punteggiatura in coda al letterale, il
    /// confronto "come inizia la rifinitura" non scatterebbe mai per queste
    /// due caselle, e la formula fissa ("La gente dice") potrebbe essere
    /// ripetuta senza che il controllo se ne accorga.
    /// </summary>
    [Fact]
    public void UnaCasellaCheRipeteLaFormulaDietroLeVirgoletteTornaGrezza()
    {
        var grezze = new[] { "a", "b", "c", "d", "e", "f", "si sapeva che finiva così", "h" };
        var rifinite = new[]
        {
            "a", "b", "c", "d", "e", "f", "La gente dice che si sapeva che finiva così", "h",
        };

        var esito = RefinementGuard.Applica(grezze, rifinite, Template);

        Assert.Equal("si sapeva che finiva così", esito[6]);
    }

    /// <summary>
    /// Vero negativo, e il caso piu' insidioso: la rifinitura comincia
    /// davvero con "La gente", proprio come il letterale del template, ma
    /// prosegue in modo diverso ("non aveva dubbi" invece di "dice") - non
    /// e' una ripetizione della formula, e' una coincidenza legittima. Se il
    /// taglio della punteggiatura in coda erodesse anche l'ultima parola
    /// vera ("dice"), il letterale si accorcerebbe a "La gente" e questa
    /// rifinitura verrebbe rifiutata per errore: e' esattamente il modo in
    /// cui le maglie si allargherebbero troppo.
    /// </summary>
    [Fact]
    public void UnaRifinituraLegittimaDietroLeVirgoletteVieneAccettata()
    {
        var grezze = new[] { "a", "b", "c", "d", "e", "f", "si sapeva che finiva così", "h" };
        var rifinite = new[]
        {
            "a", "b", "c", "d", "e", "f", "La gente non aveva dubbi: si sapeva che finiva così", "h",
        };

        var esito = RefinementGuard.Applica(grezze, rifinite, Template);

        Assert.Equal("La gente non aveva dubbi: si sapeva che finiva così", esito[6]);
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
