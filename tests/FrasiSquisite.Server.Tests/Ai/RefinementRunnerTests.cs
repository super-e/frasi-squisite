using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class RefinementRunnerTests
{
    private const string Template = "{0} {1}";

    private static RefinementRunner Crea(FakeAiTextProvider ai, int timeoutSecondi = 10) =>
        new(ai, Options.Create(new AiOptions { TimeoutSeconds = timeoutSecondi }), NullLogger<RefinementRunner>.Instance);

    [Fact]
    public async Task UnaRispostaBenFormataDiventaCaselleRifinite()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["la nonna", "con la mamma"]}]}""",
        };

        var esito = await Crea(ai).RifinisciAsync([["la nonna", "la mamma"]], Template, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["la nonna", "con la mamma"], Assert.Single(esito));
    }

    [Fact]
    public async Task IlTemplateFinisceNelMessaggioMandatoAlModello()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""" };

        await Crea(ai).RifinisciAsync([["a", "b"]], Template, CancellationToken.None);

        Assert.Contains(Template, ai.UltimoUtente!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlContenutoDelleCaselleFinisceNelMessaggio()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""" };

        await Crea(ai).RifinisciAsync([["primo", "secondo"]], Template, CancellationToken.None);

        Assert.Contains("primo", ai.UltimoUtente!, StringComparison.Ordinal);
        Assert.Contains("secondo", ai.UltimoUtente!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenzaRispostaDalModelloSiRestituisceNull()
    {
        var ai = new FakeAiTextProvider { Risposta = null };

        Assert.Null(await Crea(ai).RifinisciAsync([["a", "b"]], Template, CancellationToken.None));
    }

    [Fact]
    public async Task UnaRispostaIllegibileNonFaEsplodereNiente()
    {
        var ai = new FakeAiTextProvider { Risposta = "non sono JSON" };

        Assert.Null(await Crea(ai).RifinisciAsync([["a", "b"]], Template, CancellationToken.None));
    }

    /// <summary>
    /// I modelli incorniciano spesso il JSON in un blocco markdown: scartarlo
    /// per questo sarebbe buttare via una risposta buona.
    /// </summary>
    [Fact]
    public async Task UnJsonAvvoltoInUnBloccoMarkdownVieneComunqueLetto()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = "```json\n{\"frasi\": [{\"caselle\": [\"a\", \"con b\"]}]}\n```",
        };

        var esito = await Crea(ai).RifinisciAsync([["a", "b"]], Template, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["a", "con b"], Assert.Single(esito));
    }

    [Fact]
    public async Task OltreIlTimeoutSiRestituisceNull()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""",
            Ritardo = TimeSpan.FromSeconds(5),
        };

        var esito = await Crea(ai, timeoutSecondi: 1)
            .RifinisciAsync([["a", "b"]], Template, CancellationToken.None);

        Assert.Null(esito);
    }

    [Fact]
    public async Task UnaChiamataSolaPerTutteLeFrasi()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}, {"caselle": ["c", "d"]}]}""",
        };

        await Crea(ai).RifinisciAsync([["a", "b"], ["c", "d"]], Template, CancellationToken.None);

        Assert.Equal(1, ai.Chiamate);
    }
}
