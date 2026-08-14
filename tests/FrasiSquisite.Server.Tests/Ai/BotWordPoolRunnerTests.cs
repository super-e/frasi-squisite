using FrasiSquisite.Server.Ai;
using FrasiSquisite.Shared.Schemas;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class BotWordPoolRunnerTests
{
    // Schema compatto apposta per test leggibili: 3 caselle, non le 8 di
    // "storia". Corrisponde a src/FrasiSquisite.Shared/Schemas/Data/proverbio.json.
    private static readonly Schema Proverbio = new EmbeddedSchemaCatalog().Get("proverbio");

    private static BotWordPoolRunner Crea(FakeAiTextProvider ai) => new(ai);

    [Fact]
    public async Task UnaRispostaBenFormataDiventaParolePerRuolo()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """
                {"ruoli": [
                    {"ruolo": "Premessa", "parole": ["Chi corre troppo", "Chi tace sempre"]},
                    {"ruolo": "Conseguenza", "parole": ["inciampa due volte"]},
                    {"ruolo": "Rincaro", "parole": ["e nessuno se ne accorge"]}
                ]}
                """,
        };

        var esito = await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["Chi corre troppo", "Chi tace sempre"], esito["Premessa"]);
        Assert.Equal(["inciampa due volte"], esito["Conseguenza"]);
        Assert.Equal(["e nessuno se ne accorge"], esito["Rincaro"]);
    }

    [Fact]
    public async Task UnaParolaTroppoLungaVieneScartataMaLeAltreRestano()
    {
        var parolaTroppoLunga = new string('x', 61); // SlotTextValidator.MaxLength = 60
        var ai = new FakeAiTextProvider
        {
            Risposta = $$"""
                {"ruoli": [
                    {"ruolo": "Premessa", "parole": ["Chi corre troppo", "{{parolaTroppoLunga}}"]}
                ]}
                """,
        };

        var esito = await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["Chi corre troppo"], esito["Premessa"]);
    }

    [Fact]
    public async Task UnRuoloConSoleParoleNonValideNonCompareNelRisultato()
    {
        var parolaTroppoLunga = new string('x', 61);
        var ai = new FakeAiTextProvider
        {
            Risposta = $$"""
                {"ruoli": [
                    {"ruolo": "Premessa", "parole": ["{{parolaTroppoLunga}}"]},
                    {"ruolo": "Conseguenza", "parole": ["inciampa due volte"]}
                ]}
                """,
        };

        var esito = await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.False(esito.ContainsKey("Premessa"));
        Assert.True(esito.ContainsKey("Conseguenza"));
    }

    [Fact]
    public async Task SenzaRispostaDalModelloSiRestituisceNull()
    {
        var ai = new FakeAiTextProvider { Risposta = null };

        Assert.Null(await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None));
    }

    [Fact]
    public async Task UnaRispostaIllegibileNonFaEsplodereNiente()
    {
        var ai = new FakeAiTextProvider { Risposta = "non sono JSON" };

        Assert.Null(await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None));
    }

    /// <summary>
    /// I modelli incorniciano spesso il JSON in un blocco markdown: scartarlo
    /// per questo sarebbe buttare via una risposta buona (stesso principio
    /// verificato per RefinementRunner).
    /// </summary>
    [Fact]
    public async Task UnJsonAvvoltoInUnBloccoMarkdownVieneComunqueLetto()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = "```json\n{\"ruoli\": [{\"ruolo\": \"Premessa\", \"parole\": [\"Chi corre troppo\"]}]}\n```",
        };

        var esito = await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None);

        Assert.NotNull(esito);
        Assert.Equal(["Chi corre troppo"], esito["Premessa"]);
    }

    [Fact]
    public async Task IlPromptDiSistemaFinisceNellaChiamataAlModello()
    {
        var ai = new FakeAiTextProvider { Risposta = """{"ruoli": []}""" };

        await Crea(ai).GeneraAsync(Proverbio, CancellationToken.None);

        Assert.NotNull(ai.UltimoSistema);
        Assert.Contains("caselle", ai.UltimoUtente!, StringComparison.Ordinal);
    }
}
