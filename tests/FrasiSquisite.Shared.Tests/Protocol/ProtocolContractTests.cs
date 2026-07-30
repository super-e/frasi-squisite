using System.Text.Json;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Protocol;

public class ProtocolContractTests
{
    // Il Lotto D aggiunge NewGameRequest/BackToLobbyRequest: un client v3
    // resterebbe bloccato per sempre nella schermata finale (lotto-d-brief.md,
    // il difetto che questo lotto corregge), quindi anche qui il rifiuto
    // esplicito ("aggiorna l'app") è il comportamento giusto, non un
    // effetto collaterale da tollerare.
    [Fact]
    public void LaVersioneDiProtocolloDelLottoDE4()
    {
        Assert.Equal(4, ProtocolVersion.Current);
    }

    [Fact]
    public void UnClientDellaVersionePrecedenteNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(3));
    }

    // Un client v2 (del lotto precedente) è incompatibile tanto quanto uno
    // v3: il caso non va perso quando la versione corrente avanza, altrimenti
    // una regressione che accettasse "solo" v2 passerebbe inosservata.
    [Fact]
    public void UnClientDiDueVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(2));
    }

    // Stessa cautela per v1 (prima ancora del lotto C): la catena di
    // incompatibilità pregresse resta tutta coperta man mano che la versione
    // corrente avanza (spec del progetto: "i test che asseriscono
    // ProtocolVersion vanno aggiornati... tenendo anche i casi vecchi").
    [Fact]
    public void UnClientDiTreVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(1));
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
