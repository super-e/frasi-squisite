using FrasiSquisite.Shared.Schemas;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Schemas;

public class EmbeddedSchemaCatalogTests
{
    private readonly ISchemaCatalog _catalogo = new EmbeddedSchemaCatalog();

    [Fact]
    public void CaricaLoSchemaDiDefault()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        Assert.Equal("storia", schema.Id);
        Assert.Equal("Storia in otto atti", schema.Nome);
        Assert.Equal(8, schema.SlotCount);
    }

    [Fact]
    public void CaricaLoSchemaSurrealistaClassico()
    {
        var schema = _catalogo.Get("surrealista-classico");

        Assert.Equal("Surrealista classico", schema.Nome);
        Assert.Equal(5, schema.SlotCount);
    }

    [Fact]
    public void OgniCasellaHaRuoloPromptEdEsempioNonVuoti()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        Assert.All(schema.Caselle, casella =>
        {
            Assert.False(string.IsNullOrWhiteSpace(casella.Ruolo));
            Assert.False(string.IsNullOrWhiteSpace(casella.Prompt));
            Assert.False(string.IsNullOrWhiteSpace(casella.Esempio));
        });
    }

    [Fact]
    public void ComponeLaFraseSecondoIlTemplate()
    {
        var schema = _catalogo.Get("surrealista-classico");

        var frase = schema.Compose(["Il cadavere", "squisito", "berrà", "il vino", "nuovo"]);

        Assert.Equal("Il cadavere squisito berrà il vino nuovo", frase);
    }

    /// <summary>
    /// Il default è il primo schema con testo fisso nel template (gli altri
    /// cinque sono semplici concatenazioni "{0} {1} …"): serve un test che
    /// verifichi che le congiunzioni finiscano davvero nella frase composta,
    /// e nella posizione giusta.
    /// </summary>
    [Fact]
    public void LoSchemaDiDefaultIntercalaLeCongiunzioniDelTemplate()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        var frase = schema.Compose([
            "Un pinguino in doppiopetto",
            "insieme al suo commercialista",
            "nella sala d'attesa di un dentista",
            "monta una libreria svedese",
            "perché gliel'ha detto l'oroscopo",
            "Non è colpa mia, io ho solo firmato",
            "Si sapeva che finiva così",
            "sono finiti tutti al telegiornale",
        ]);

        Assert.Equal(
            "Un pinguino in doppiopetto insieme al suo commercialista " +
            "nella sala d'attesa di un dentista monta una libreria svedese, " +
            "perché gliel'ha detto l'oroscopo. " +
            "Ultime parole: «Non è colpa mia, io ho solo firmato». " +
            "Il pubblico: «Si sapeva che finiva così». " +
            "Alla fine sono finiti tutti al telegiornale.",
            frase);
    }

    [Fact]
    public void ComporreConUnNumeroSbagliatoDiValoriFallisce()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        Assert.Throws<ArgumentException>(() => schema.Compose(["uno", "due"]));
    }

    [Fact]
    public void ChiedereUnoSchemaInesistenteFallisceConMessaggioUtile()
    {
        var eccezione = Assert.Throws<KeyNotFoundException>(() => _catalogo.Get("non-esiste"));

        Assert.Contains("non-esiste", eccezione.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IlCatalogoEspoheTuttiGliSchemiCaricati()
    {
        Assert.NotEmpty(_catalogo.All);
        Assert.Contains(_catalogo.All, s => s.Id == Schema.DefaultId);
    }

    // ================= Lotto C: catalogo a più schemi =================

    [Fact]
    public void IlCatalogoCaricaTuttiGliSchemi()
    {
        Assert.Equal(6, _catalogo.All.Count);
        Assert.Contains(_catalogo.All, s => s.Id == "storia");
        Assert.Contains(_catalogo.All, s => s.Id == "surrealista-classico");
        Assert.Contains(_catalogo.All, s => s.Id == "titolo-di-giornale");
        Assert.Contains(_catalogo.All, s => s.Id == "proverbio");
        Assert.Contains(_catalogo.All, s => s.Id == "oroscopo");
        Assert.Contains(_catalogo.All, s => s.Id == "ricetta");
    }

    /// <summary>
    /// Il default per primo, gli altri in ordine alfabetico di nome
    /// (lotto-c-brief.md, §Ordinamento del catalogo). Un ordine letto
    /// direttamente da Assembly.GetManifestResourceNames() non lo
    /// garantirebbe.
    /// </summary>
    [Fact]
    public void LOrdineDelCatalogoEDeterministicoConIlDefaultPerPrimo()
    {
        var atteso = new[]
        {
            "storia",               // default, sempre primo
            "oroscopo",             // "Oroscopo del giorno"
            "proverbio",            // "Proverbio della nonna"
            "ricetta",              // "Ricetta d'autore"
            "surrealista-classico", // "Surrealista classico"
            "titolo-di-giornale",   // "Titolo di giornale"
        };

        Assert.Equal(atteso, _catalogo.All.Select(s => s.Id));
    }

    /// <summary>
    /// Costruzioni ripetute del catalogo (es. richieste diverse al server)
    /// devono produrre sempre lo stesso ordine: se GetManifestResourceNames()
    /// tornasse un ordine diverso a ogni chiamata, questo test lo scoprirebbe
    /// dove un singolo confronto con una lista attesa non basterebbe.
    /// </summary>
    [Fact]
    public void LOrdineRestaLoStessoInCostruzioniRipetute()
    {
        var ordini = Enumerable.Range(0, 10)
            .Select(_ => new EmbeddedSchemaCatalog().All.Select(s => s.Id).ToList())
            .ToList();

        Assert.All(ordini, ordine => Assert.Equal(ordini[0], ordine));
    }

    public static IEnumerable<object[]> TuttiGliSchemi() =>
        new EmbeddedSchemaCatalog().All.Select(s => new object[] { s });

    /// <summary>
    /// Parametrico su catalogo.All (lotto-c-brief.md): vale per i quattro
    /// schemi nuovi e continuerà a valere per qualunque schema aggiunto in
    /// futuro, senza dover scrivere un nuovo test per ciascuno.
    /// </summary>
    [Theory]
    [MemberData(nameof(TuttiGliSchemi))]
    public void IlNumeroDiSegnapostoDelTemplateCombaciaConLeCaselle(Schema schema)
    {
        // Il template è generato in ordine ({0} {1} {2}…, mai scritto a mano
        // in modo non lineare): comporre con tanti valori quante le caselle
        // deve sempre riuscire, e mai lanciare FormatException.
        var valori = Enumerable.Range(0, schema.SlotCount).Select(i => $"valore{i}").ToList();

        var frase = schema.Compose(valori);

        Assert.All(valori, valore => Assert.Contains(valore, frase, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TuttiGliSchemi))]
    public void OgniCasellaDiOgniSchemaHaRuoloPromptEdEsempioNonVuoti(Schema schema)
    {
        Assert.All(schema.Caselle, casella =>
        {
            Assert.False(string.IsNullOrWhiteSpace(casella.Ruolo));
            Assert.False(string.IsNullOrWhiteSpace(casella.Prompt));
            Assert.False(string.IsNullOrWhiteSpace(casella.Esempio));
        });
    }

    [Theory]
    [MemberData(nameof(TuttiGliSchemi))]
    public void ComporreFunzionaPerOgniSchemaConIlNumeroGiustoDiValori(Schema schema)
    {
        var valori = Enumerable.Range(0, schema.SlotCount).Select(i => $"valore{i}").ToList();

        var frase = schema.Compose(valori);

        Assert.False(string.IsNullOrWhiteSpace(frase));
    }

    /// <summary>
    /// Otto caselle vuol dire otto round: è lo schema più lungo del catalogo,
    /// e il numero è la cosa che conta per il motore (K = SlotCount).
    /// </summary>
    [Fact]
    public void LoSchemaStoriaHaOttoCaselle()
    {
        var schema = _catalogo.Get("storia");

        Assert.Equal("Storia in otto atti", schema.Nome);
        Assert.Equal(8, schema.SlotCount);
    }

    [Fact]
    public void LoSchemaTitoloDiGiornaleHaQuattroCaselle()
    {
        var schema = _catalogo.Get("titolo-di-giornale");

        Assert.Equal("Titolo di giornale", schema.Nome);
        Assert.Equal(4, schema.SlotCount);
        Assert.Equal(
            "Il sindaco ha inaugurato una rotonda in pigiama",
            schema.Compose(["Il sindaco", "ha inaugurato", "una rotonda", "in pigiama"]));
    }

    [Fact]
    public void LoSchemaProverbioHaTreCaselle()
    {
        var schema = _catalogo.Get("proverbio");

        Assert.Equal("Proverbio della nonna", schema.Nome);
        Assert.Equal(3, schema.SlotCount);
        Assert.Equal(
            "Chi va piano mangia le pere e non ringrazia",
            schema.Compose(["Chi va piano", "mangia le pere", "e non ringrazia"]));
    }

    [Fact]
    public void LoSchemaOroscopoHaCinqueCaselle()
    {
        var schema = _catalogo.Get("oroscopo");

        Assert.Equal("Oroscopo del giorno", schema.Nome);
        Assert.Equal(5, schema.SlotCount);
        Assert.Equal(
            "Vergine un incontro inatteso in ascensore non fidarti dei mercoledì il 47",
            schema.Compose(["Vergine", "un incontro inatteso", "in ascensore", "non fidarti dei mercoledì", "il 47"]));
    }

    [Fact]
    public void LoSchemaRicettaHaCinqueCaselle()
    {
        var schema = _catalogo.Get("ricetta");

        Assert.Equal("Ricetta d'autore", schema.Nome);
        Assert.Equal(5, schema.SlotCount);
        Assert.Equal(
            "Tre cipolle vanno frullate con rabbia del bicarbonato tiepido, ai nemici",
            schema.Compose(["Tre cipolle", "vanno frullate", "con rabbia", "del bicarbonato", "tiepido, ai nemici"]));
    }
}
