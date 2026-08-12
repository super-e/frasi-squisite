using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class RefinementRunnerTests
{
    private const string Template = "{0} {1}";

    private static readonly string[] Ruoli = ["Soggetto", "Predicato"];

    private static RefinementRunner Crea(
        FakeAiTextProvider ai,
        int timeoutSecondi = 15,
        int timeoutSecondiPerFraseAggiuntiva = 3,
        int timeoutMassimoSecondi = 30) =>
        new(ai, Options.Create(new AiOptions
        {
            TimeoutSeconds = timeoutSecondi,
            TimeoutSecondiPerFraseAggiuntiva = timeoutSecondiPerFraseAggiuntiva,
            TimeoutMassimoSecondi = timeoutMassimoSecondi,
        }), NullLogger<RefinementRunner>.Instance);

    [Fact]
    public async Task UnaRispostaBenFormataDiventaCaselleRifinite()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["la nonna", "con la mamma"]}]}""",
        };

        var esito = await Crea(ai).RifinisciAsync([["la nonna", "la mamma"]], Template, Ruoli, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["la nonna", "con la mamma"], Assert.Single(esito));
    }

    [Fact]
    public async Task IlTemplateFinisceNelMessaggioMandatoAlModello()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""" };

        await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

        Assert.Contains(Template, ai.UltimoUtente!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlContenutoDelleCaselleFinisceNelMessaggio()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""" };

        await Crea(ai).RifinisciAsync([["primo", "secondo"]], Template, Ruoli, CancellationToken.None);

        Assert.Contains("primo", ai.UltimoUtente!, StringComparison.Ordinal);
        Assert.Contains("secondo", ai.UltimoUtente!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IRuoliFinisconoNelMessaggioMandatoAlModello()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""" };

        await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

        Assert.Contains("Soggetto", ai.UltimoUtente!, StringComparison.Ordinal);
        Assert.Contains("Predicato", ai.UltimoUtente!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenzaRispostaDalModelloSiRestituisceNull()
    {
        var ai = new FakeAiTextProvider { Risposta = null };

        Assert.Null(await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None));
    }

    [Fact]
    public async Task UnaRispostaIllegibileNonFaEsplodereNiente()
    {
        var ai = new FakeAiTextProvider { Risposta = "non sono JSON" };

        Assert.Null(await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None));
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

        var esito = await Crea(ai).RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

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
            .RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

        Assert.Null(esito);
    }

    [Fact]
    public async Task UnaChiamataSolaPerTutteLeFrasi()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}, {"caselle": ["c", "d"]}]}""",
        };

        await Crea(ai).RifinisciAsync([["a", "b"], ["c", "d"]], Template, Ruoli, CancellationToken.None);

        Assert.Equal(1, ai.Chiamate);
    }

    /// <summary>
    /// Con una sola frase il tempo concesso e' quello base: nessun
    /// incremento per frasi aggiuntive da applicare.
    /// </summary>
    [Fact]
    public async Task ConUnaSolaFraseIlTimeoutEQuelloBase()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""",
            Ritardo = TimeSpan.FromMilliseconds(1500),
        };

        var esito = await Crea(ai, timeoutSecondi: 1, timeoutSecondiPerFraseAggiuntiva: 1)
            .RifinisciAsync([["a", "b"]], Template, Ruoli, CancellationToken.None);

        Assert.Null(esito);
    }

    /// <summary>
    /// Le stesse impostazioni del test precedente, ma con piu' frasi: il
    /// tempo concesso cresce a sufficienza da reggere lo stesso ritardo che
    /// con una sola frase avrebbe fatto scadere la chiamata.
    /// </summary>
    [Fact]
    public async Task ConPiuFrasiIlTimeoutSiAllunga()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}, {"caselle": ["c", "d"]}, {"caselle": ["e", "f"]}, {"caselle": ["g", "h"]}]}""",
            Ritardo = TimeSpan.FromMilliseconds(1500),
        };

        var esito = await Crea(ai, timeoutSecondi: 1, timeoutSecondiPerFraseAggiuntiva: 1)
            .RifinisciAsync([["a", "b"], ["c", "d"], ["e", "f"], ["g", "h"]], Template, Ruoli, CancellationToken.None);

        Assert.NotNull(esito);
    }

    /// <summary>
    /// Il tetto ferma la crescita: con molte frasi il tempo concesso non
    /// supera mai TimeoutMassimoSecondi, anche se la formula senza tetto
    /// darebbe un numero piu' grande.
    /// </summary>
    [Fact]
    public async Task IlTimeoutNonSuperaIlTetto()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""",
            Ritardo = TimeSpan.FromSeconds(3),
        };

        var esito = await Crea(ai, timeoutSecondi: 1, timeoutSecondiPerFraseAggiuntiva: 1, timeoutMassimoSecondi: 2)
            .RifinisciAsync(
                [["a", "b"], ["c", "d"], ["e", "f"], ["g", "h"], ["i", "l"]],
                Template,
                Ruoli,
                CancellationToken.None);

        Assert.Null(esito);
    }
}
