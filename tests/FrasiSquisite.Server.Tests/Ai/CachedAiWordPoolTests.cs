using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Server.Ai;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class CachedAiWordPoolTests
{
    [Fact]
    public void UnRuoloPresenteInCacheRestituisceUnaVoceDellaCache()
    {
        var pool = new CachedAiWordPool(new StaticWordPool());
        pool.Popola("Soggetto", ["Il notaio col paracadute"]);

        var parola = pool.Take("Soggetto", new SeededRandomSource(1));

        Assert.Equal("Il notaio col paracadute", parola);
    }

    [Fact]
    public void UnRuoloAssenteDallaCacheRicadeSulFallback()
    {
        var pool = new CachedAiWordPool(new StaticWordPool());

        var parola = pool.Take("Soggetto", new SeededRandomSource(1));

        var opzioniAspettate = new List<string> { "Il notaio", "La pantofola", "Un tram", "Il vescovo", "La zuppa", "Un ombrello" };
        Assert.Contains(parola, opzioniAspettate);
    }

    [Fact]
    public void PopolareConUnaListaVuotaNonSostituisceIlFallback()
    {
        var pool = new CachedAiWordPool(new StaticWordPool());
        pool.Popola("Soggetto", []);

        var parola = pool.Take("Soggetto", new SeededRandomSource(1));

        var opzioniAspettate = new List<string> { "Il notaio", "La pantofola", "Un tram", "Il vescovo", "La zuppa", "Un ombrello" };
        Assert.Contains(parola, opzioniAspettate);
    }

    [Fact]
    public void PopolareDiNuovoLoStessoRuoloSostituisceLaVoceDiCachePrecedente()
    {
        var pool = new CachedAiWordPool(new StaticWordPool());
        pool.Popola("Soggetto", ["prima voce"]);
        pool.Popola("Soggetto", ["seconda voce"]);

        var parola = pool.Take("Soggetto", new SeededRandomSource(1));

        Assert.Equal("seconda voce", parola);
    }
}
