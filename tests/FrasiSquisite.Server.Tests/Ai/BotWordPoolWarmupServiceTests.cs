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
    public async Task UnGiroCheRisponeSempreSvuotaGliSchemiDaRiempire()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": [{"ruolo": "Soggetto", "parole": ["prova"]}]}""" };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = Crea(ai, cache);
        var daRiempire = new HashSet<string>(Catalogo.All.Select(s => s.Id));

        var restano = await servizio.EseguiUnGiroAsync(daRiempire, CancellationToken.None);

        Assert.Empty(restano);
    }

    [Fact]
    public async Task UnGiroCheNonRisponeMaiLasciaTuttiGliSchemiDaRiempire()
    {
        var ai = new FakeAiTextProvider { Risposta = null };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = Crea(ai, cache);
        var daRiempire = new HashSet<string>(Catalogo.All.Select(s => s.Id));

        var restano = await servizio.EseguiUnGiroAsync(daRiempire, CancellationToken.None);

        Assert.Equal(daRiempire, restano);
    }

    [Fact]
    public async Task UnGiroCheRisponePopolaDavveroLaCache()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": [{"ruolo": "Soggetto", "parole": ["voce di prova dalla cache"]}]}""" };
        var cache = new CachedAiWordPool(new StaticWordPool());
        var servizio = Crea(ai, cache);

        await servizio.EseguiUnGiroAsync(new HashSet<string>(Catalogo.All.Select(s => s.Id)), CancellationToken.None);

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
}
