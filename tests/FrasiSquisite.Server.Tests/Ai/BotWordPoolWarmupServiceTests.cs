using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Server.Ai;
using FrasiSquisite.Shared.Schemas;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class BotWordPoolWarmupServiceTests
{
    private static readonly ISchemaCatalog Catalogo = new EmbeddedSchemaCatalog();

    private static BotWordPoolWarmupService Crea(FakeAiTextProvider ai, CachedAiWordPool cache) =>
        new(Catalogo, new BotWordPoolRunner(ai), cache, NullLogger<BotWordPoolWarmupService>.Instance);

    [Fact]
    public async Task UnGiroCheRispondeSempreSvuotaGliSchemiDaRiempire()
    {
        // Schema a un solo ruolo apposta: la risposta finta ne copre
        // esattamente uno solo, e con la copertura completa richiesta dal
        // fix, questo è l'unico modo di far svuotare un giro con una
        // risposta fissa uguale per ogni schema (il catalogo vero ha schemi
        // multi-ruolo, che questa stessa risposta lascerebbe parzialmente
        // scoperti).
        var schema = SchemaConUnSoloRuolo();
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": [{"ruolo": "Soggetto", "parole": ["prova"]}]}""" };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = new BotWordPoolWarmupService(
            new UnSoloSchemaCatalog(schema), new BotWordPoolRunner(ai), cache, NullLogger<BotWordPoolWarmupService>.Instance);
        var daRiempire = new HashSet<string> { schema.Id };

        var restano = await servizio.EseguiUnGiroAsync(daRiempire, CancellationToken.None);

        Assert.Empty(restano);
    }

    [Fact]
    public async Task UnGiroCheNonRispondeMaiLasciaTuttiGliSchemiDaRiempire()
    {
        var ai = new FakeAiTextProvider { Risposta = null };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = Crea(ai, cache);
        var daRiempire = new HashSet<string>(Catalogo.All.Select(s => s.Id));

        var restano = await servizio.EseguiUnGiroAsync(daRiempire, CancellationToken.None);

        Assert.Equal(daRiempire, restano);
    }

    [Fact]
    public async Task UnGiroCheRispondePopolaDavveroLaCache()
    {
        // Stesso schema a un solo ruolo di UnGiroCheRispondeSempreSvuotaGliSchemiDaRiempire,
        // per lo stesso motivo: la copertura completa richiesta dal fix va
        // garantita anche qui.
        var schema = SchemaConUnSoloRuolo();
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": [{"ruolo": "Soggetto", "parole": ["voce di prova dalla cache"]}]}""" };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = new BotWordPoolWarmupService(
            new UnSoloSchemaCatalog(schema), new BotWordPoolRunner(ai), cache, NullLogger<BotWordPoolWarmupService>.Instance);

        await servizio.EseguiUnGiroAsync(new HashSet<string> { schema.Id }, CancellationToken.None);

        Assert.Equal("voce di prova dalla cache", cache.Take("Soggetto", new SeededRandomSource(1)));
    }

    [Fact]
    public async Task UnaSolaChiamataPerSchema()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": []}""" };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = Crea(ai, cache);

        await servizio.EseguiUnGiroAsync(new HashSet<string>(Catalogo.All.Select(s => s.Id)), CancellationToken.None);

        Assert.Equal(Catalogo.All.Count, ai.Chiamate);
    }

    /// <summary>
    /// Una risposta vuota ("ruoli": []) è JSON valido ma non copre nessun
    /// ruolo dello schema: prima del fix restava comunque marcata come
    /// riempita per sempre, lasciando quei ruoli inchiodati al dizionario
    /// statico senza che nessuno se ne accorgesse.
    /// </summary>
    [Fact]
    public async Task UnaRispostaVuotaLasciaLoSchemaTraQuelliDaRiempire()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": []}""" };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = Crea(ai, cache);
        var daRiempire = new HashSet<string>(Catalogo.All.Select(s => s.Id));

        var restano = await servizio.EseguiUnGiroAsync(daRiempire, CancellationToken.None);

        Assert.Equal(daRiempire, restano);
    }

    /// <summary>
    /// Una risposta che copre solo alcuni dei ruoli dello schema (es. 1 su 3
    /// per "proverbio") non basta a considerarlo riempito: senza questo
    /// controllo i ruoli mancanti resterebbero silenziosamente sul
    /// dizionario statico per sempre (whole-branch review).
    /// </summary>
    [Fact]
    public async Task UnaRispostaParzialeLasciaLoSchemaTraQuelliDaRiempire()
    {
        var proverbio = new EmbeddedSchemaCatalog().Get("proverbio");
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": [{"ruolo": "Premessa", "parole": ["Chi corre troppo"]}]}""" };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = new BotWordPoolWarmupService(
            new UnSoloSchemaCatalog(proverbio), new BotWordPoolRunner(ai), cache, NullLogger<BotWordPoolWarmupService>.Instance);
        var daRiempire = new HashSet<string> { proverbio.Id };

        var restano = await servizio.EseguiUnGiroAsync(daRiempire, CancellationToken.None);

        Assert.Equal(daRiempire, restano);
    }

    private static Schema SchemaConUnSoloRuolo() => new(
        "prova", 1, "Prova", [new Casella("Soggetto", "prompt", "esempio")], "{0}");

    /// <summary>Catalogo finto con un solo schema, per test che vogliono controllare esattamente i suoi ruoli.</summary>
    private sealed class UnSoloSchemaCatalog(Schema schema) : ISchemaCatalog
    {
        public IReadOnlyList<Schema> All { get; } = [schema];

        public Schema Get(string id) => schema;
    }
}
