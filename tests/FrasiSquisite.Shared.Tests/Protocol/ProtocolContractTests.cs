using System.Text.Json;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Protocol;

public class ProtocolContractTests
{
    // L'illustrazione via IA (AI Task 1) porta il protocollo a v7:
    // IllustrationReadyMessage e IllustrationFailedMessage sono messaggi nuovi
    // che un client v6 non saprebbe interpretare, restando con il pulsante
    // dell'illustrazione spento invece di mostrare l'esito. Anche qui il
    // rifiuto esplicito ("aggiorna l'app") è il comportamento giusto.
    [Fact]
    public void LaVersioneDelProtocolloE8()
    {
        Assert.Equal(8, ProtocolVersion.Current);
    }

    // v6 è l'unica versione davvero installata sul campo: l'APK del lotto
    // precedente, uscito prima che l'illustrazione portasse il protocollo a
    // v7. Questo caso era rimasto scoperto quando Current è avanzato: la
    // convenzione del file (allungare la catena a ogni avanzamento, senza
    // perdere i casi vecchi) impone di aggiungerlo qui, in cima, e di
    // rinumerare "quante versioni prima" tutti i casi già coperti.
    [Fact]
    public void UnClientDellaVersionePrecedenteNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(6));
    }

    // Un client v5 è incompatibile tanto quanto uno v6: il caso non va perso
    // quando la versione corrente avanza, altrimenti una regressione che
    // accettasse "solo" v5 passerebbe inosservata.
    [Fact]
    public void UnClientDiDueVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(5));
    }

    // Stessa cautela per v4: la catena di incompatibilità pregresse resta
    // tutta coperta man mano che la versione corrente avanza (spec del
    // progetto: "i test che asseriscono ProtocolVersion vanno aggiornati...
    // tenendo anche i casi vecchi").
    [Fact]
    public void UnClientDiTreVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(4));
    }

    // E per v3.
    [Fact]
    public void UnClientDiQuattroVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(3));
    }

    // E per v2.
    [Fact]
    public void UnClientDiCinqueVersioniPrimaNonECompatibile()
    {
        Assert.False(ProtocolVersion.IsCompatible(2));
    }

    // E per v1, la prima versione mai esistita.
    [Fact]
    public void UnClientDiSeiVersioniPrimaNonECompatibile()
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
    public void RoundtripDiRevealFragment()
    {
        var originale = new RevealFragment(IsSlot: true, Text: "Il cadavere", IsRevealed: true);

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<RevealFragment>(json, ProtocolJson.Options);

        Assert.Equal(originale, ricostruito);
    }

    [Fact]
    public void RoundtripDiRevealStep()
    {
        var originale = new RevealStepMessage(
            PhraseIndex: 0,
            TotalPhrases: 3,
            Fragments:
            [
                new RevealFragment(true, "Il cadavere", true),
                new RevealFragment(false, " ", true),
                new RevealFragment(true, string.Empty, false),
            ],
            PhraseComplete: false);

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<RevealStepMessage>(json, ProtocolJson.Options);

        Assert.NotNull(ricostruito);
        Assert.Equal(originale.PhraseIndex, ricostruito.PhraseIndex);
        Assert.Equal(originale.TotalPhrases, ricostruito.TotalPhrases);
        Assert.Equal(originale.Fragments, ricostruito.Fragments);
        Assert.Equal(originale.PhraseComplete, ricostruito.PhraseComplete);
    }

    /// <summary>
    /// Il tipo non deve avere un campo per gli autori: è così che la
    /// segretezza è garantita dal tipo e non dalla disciplina (spec §3).
    /// Come <see cref="SlotRequestNonEspoheAlcunCampoDiTesto"/>, l'elenco
    /// completo delle proprietà: cercare solo "Authors" per nome (come
    /// faceva questo test) lascerebbe passare indisturbato un campo
    /// rimesso con un altro nome (es. "Autori", "AuthorNames",
    /// "AuthorIds") - una regressione che riaprirebbe la fuga senza che
    /// nessun altro test se ne accorga, perché il valore sarebbe comunque
    /// vuoto nei casi provati.
    /// </summary>
    [Fact]
    public void IlPassoDiRevealNonHaUnCampoAutori()
    {
        var proprieta = typeof(RevealStepMessage).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(
            ["PhraseIndex", "TotalPhrases", "Fragments", "PhraseComplete"],
            proprieta);
    }

    // Stesso motivo di RoundtripDiRoomState: PhraseResultView contiene una
    // lista (Authors), quindi l'uguaglianza di record confronta quella lista
    // per riferimento (anzi qui anche per tipo concreto: il literal produce
    // un array a sola lettura del compilatore, la deserializzazione una
    // List<string>) e fallirebbe anche a contenuto identico. Il roundtrip si
    // verifica quindi campo per campo.
    [Fact]
    public void RoundtripDelMessaggioFinale()
    {
        var originale = new GameFinishedMessage([
            new PhraseResultView(1, "Il notaio divora il tramonto", ["Anna", "Bruno"], 2, true),
            new PhraseResultView(0, "La zuppa scavalca una scala", ["Bruno", "Anna"], 0, false),
        ]);

        var json = JsonSerializer.Serialize(originale, ProtocolJson.Options);
        var ricostruito = JsonSerializer.Deserialize<GameFinishedMessage>(json, ProtocolJson.Options);

        Assert.NotNull(ricostruito);
        Assert.Equal(2, ricostruito.Results.Count);

        for (var i = 0; i < originale.Results.Count; i++)
        {
            var atteso = originale.Results[i];
            var effettivo = ricostruito.Results[i];

            Assert.Equal(atteso.PhraseIndex, effettivo.PhraseIndex);
            Assert.Equal(atteso.Text, effettivo.Text);
            Assert.Equal(atteso.Authors, effettivo.Authors);
            Assert.Equal(atteso.Votes, effettivo.Votes);
            Assert.Equal(atteso.IsWinner, effettivo.IsWinner);
        }
    }
}
