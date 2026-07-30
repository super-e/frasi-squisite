using System.Text.Json;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Protocol;

public class ProtocolContractTests
{
    // Il Lotto C aggiunge AvailableSchemas a RoomStateMessage: un campo in
    // più a un record è tollerato dalla serializzazione in entrambe le
    // direzioni, ma un client v2 non saprebbe mostrare il selettore di
    // schema. Meglio un rifiuto esplicito ("aggiorna l'app") che una lobby
    // incompleta senza dirlo (lotto-c-brief.md).
    [Fact]
    public void LaVersioneDiProtocolloDelLottoCE3()
    {
        Assert.Equal(3, ProtocolVersion.Current);
    }

    [Fact]
    public void UnClientDellaVersionePrecedenteNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(2));
    }

    [Fact]
    public void SlotRequestSiSerializzaInCamelCase()
    {
        var messaggio = new SlotRequestMessage(
            Round: 0,
            TotalRounds: 5,
            Ruolo: "Soggetto",
            Prompt: "Un soggetto, con l'articolo",
            Esempio: "Il cadavere");

        var json = JsonSerializer.Serialize(messaggio, ProtocolJson.Options);

        Assert.Contains("\"round\":0", json, StringComparison.Ordinal);
        Assert.Contains("\"totalRounds\":5", json, StringComparison.Ordinal);
        Assert.Contains("\"ruolo\":\"Soggetto\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Test di segretezza a livello di contratto: se un giorno qualcuno
    /// aggiungesse alla richiesta di casella un campo con il testo della frase,
    /// questo test fallirebbe. Vedi spec §2.3.
    /// </summary>
    [Fact]
    public void SlotRequestNonEspoheAlcunCampoDiTesto()
    {
        var proprieta = typeof(SlotRequestMessage).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(
            ["Round", "TotalRounds", "Ruolo", "Prompt", "Esempio"],
            proprieta);
    }

    // Nota: i record che contengono liste non hanno uguaglianza strutturale
    // (i record confrontano le liste per riferimento), quindi il roundtrip si
    // verifica campo per campo. Le singole liste si confrontano con
    // Assert.Equal, che sulle collezioni confronta gli elementi.
    [Fact]
    public void RoundtripDiRoomState()
    {
        var originale = new RoomStateMessage(
            RoomCode: "ABCD",
            Phase: "Lobby",
            Players: [new PlayerView(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Enrico", true, true, false)],
            SchemaId: "surrealista-classico",
            SlotCount: 5);

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<RoomStateMessage>(json, ProtocolJson.Options);

        Assert.NotNull(ricostruito);
        Assert.Equal(originale.RoomCode, ricostruito.RoomCode);
        Assert.Equal(originale.Phase, ricostruito.Phase);
        Assert.Equal(originale.SchemaId, ricostruito.SchemaId);
        Assert.Equal(originale.SlotCount, ricostruito.SlotCount);
        Assert.Equal(originale.Players, ricostruito.Players);
    }

    [Fact]
    public void RoundtripDiPlayerViewConIsBot()
    {
        var originale = new PlayerView(Guid.NewGuid(), "Bot Ada", false, false, true);

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<PlayerView>(json, ProtocolJson.Options);

        Assert.Equal(originale, ricostruito);
    }

    [Fact]
    public void RoundtripDiRevealStep()
    {
        var originale = new RevealStepMessage(
            PhraseIndex: 0,
            TotalPhrases: 3,
            RevealedSlots: ["Il cadavere", "squisito"],
            PhraseComplete: false,
            Authors: []);

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<RevealStepMessage>(json, ProtocolJson.Options);

        Assert.NotNull(ricostruito);
        Assert.Equal(originale.PhraseIndex, ricostruito.PhraseIndex);
        Assert.Equal(originale.TotalPhrases, ricostruito.TotalPhrases);
        Assert.Equal(originale.RevealedSlots, ricostruito.RevealedSlots);
        Assert.Equal(originale.PhraseComplete, ricostruito.PhraseComplete);
        Assert.Equal(originale.Authors, ricostruito.Authors);
    }
}
