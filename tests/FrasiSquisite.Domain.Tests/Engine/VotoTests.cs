using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class VotoTests
{
    private const int N = 3;
    private const int K = 3;

    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    /// <summary>Partita portata fino all'ultimo passo di reveal incluso.</summary>
    private GameState AlVoto()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < N; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }

        for (var i = 0; i < N * K; i++)
        {
            stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        }

        return stato;
    }

    [Fact]
    public void DopoLUltimoRevealLaPartitaEntraInVoto()
    {
        Assert.Equal(RoomPhase.Voting, AlVoto().Phase);
    }

    [Fact]
    public void LUltimoPassoDiRevealMandaLeFrasiDaVotare()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < N; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }

        EngineResult ultimo = null!;
        for (var i = 0; i < N * K; i++)
        {
            ultimo = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
            stato = ultimo.State;
        }

        var richiesta = Assert.Single(ultimo.Broadcasts<VoteRequestMessage>());

        Assert.Equal(N, richiesta.Phrases.Count);
        Assert.All(richiesta.Phrases, f => Assert.False(string.IsNullOrWhiteSpace(f)));
    }

    /// <summary>
    /// Il messaggio finale non deve più partire alla fine del reveal: arriva
    /// solo alla chiusura del voto. Se partisse qui, i client salterebbero
    /// direttamente alla classifica senza aver votato.
    /// </summary>
    [Fact]
    public void LUltimoPassoDiRevealNonChiudeAncoraLaPartita()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < N; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }

        EngineResult ultimo = null!;
        for (var i = 0; i < N * K; i++)
        {
            ultimo = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
            stato = ultimo.State;
        }

        Assert.Empty(ultimo.Broadcasts<GameFinishedMessage>());
    }

    [Fact]
    public void EntrandoInVotoNessunoHaAncoraVotato()
    {
        Assert.Empty(AlVoto().Votes);
    }

    [Fact]
    public void TornareAllaLobbyAzzeraIVoti()
    {
        var stato = AlVoto();
        stato = stato with { Votes = new Dictionary<Guid, int> { [Giocatore(0)] = 1 }, Phase = RoomPhase.Finished };

        var azzerato = _motore.Handle(stato, new BackToLobbyRequested(Giocatore(0))).State;

        Assert.Empty(azzerato.Votes);
    }

    [Fact]
    public void UnVotoValidoVieneRegistrato()
    {
        var stato = AlVoto();

        var risultato = _motore.Handle(stato, new VoteCast(Giocatore(0), 1));

        Assert.Equal(1, risultato.State.Votes[Giocatore(0)]);
    }

    [Fact]
    public void FinchePiuDiUnoDeveVotareArrivaSoloLAvanzamento()
    {
        var stato = AlVoto();

        var risultato = _motore.Handle(stato, new VoteCast(Giocatore(0), 1));

        var avanzamento = Assert.Single(risultato.Broadcasts<VoteProgressMessage>());
        Assert.Equal(1, avanzamento.Voted);
        Assert.Equal(N, avanzamento.Total);
        Assert.Empty(risultato.Broadcasts<GameFinishedMessage>());
    }

    [Fact]
    public void QuandoHannoVotatoTuttiLaPartitaSiChiude()
    {
        var stato = AlVoto();
        stato = _motore.Handle(stato, new VoteCast(Giocatore(0), 1)).State;
        stato = _motore.Handle(stato, new VoteCast(Giocatore(1), 1)).State;

        var risultato = _motore.Handle(stato, new VoteCast(Giocatore(2), 0));

        Assert.Equal(RoomPhase.Finished, risultato.State.Phase);

        var finale = Assert.Single(risultato.Broadcasts<GameFinishedMessage>());
        Assert.Equal(N, finale.Results.Count);
        Assert.Equal(1, finale.Results[0].PhraseIndex);
        Assert.Equal(2, finale.Results[0].Votes);
        Assert.True(finale.Results[0].IsWinner);
    }

    /// <summary>
    /// Non basta che il punteggio in cima sia giusto: deve essere il
    /// punteggio DELLA frase 1, non della frase in posizione 0. Un motore che
    /// confondesse posizione in classifica con indice di frase (bug segnalato
    /// in revisione: finora <c>Classifica</c> era stata esercitata solo con
    /// voti vuoti, dove l'ordinamento è l'identità e l'errore sarebbe stato
    /// invisibile) supererebbe comunque i test sui soli punteggi. Il testo
    /// atteso è quello mandato dal server stesso in <see cref="VoteRequestMessage"/>
    /// all'ingresso in voto, nello stesso ordine — non un valore ricalcolato
    /// a mano nel test.
    /// </summary>
    [Fact]
    public void LaRigaInCimaAllaClassificaPortaTestoEAutoriDellaFraseGiusta()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < N; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }

        EngineResult ultimoReveal = null!;
        for (var i = 0; i < N * K; i++)
        {
            ultimoReveal = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
            stato = ultimoReveal.State;
        }

        var testiAttesi = Assert.Single(ultimoReveal.Broadcasts<VoteRequestMessage>()).Phrases;

        stato = _motore.Handle(stato, new VoteCast(Giocatore(0), 1)).State;
        stato = _motore.Handle(stato, new VoteCast(Giocatore(1), 1)).State;

        var risultato = _motore.Handle(stato, new VoteCast(Giocatore(2), 0));

        var finale = Assert.Single(risultato.Broadcasts<GameFinishedMessage>());

        Assert.Equal(1, finale.Results[0].PhraseIndex);
        Assert.Equal(testiAttesi[1], finale.Results[0].Text);
    }

    [Fact]
    public void LaClassificaPortaGliAutoriDiOgniFrase()
    {
        var stato = AlVoto();

        EngineResult ultimo = null!;
        for (var i = 0; i < N; i++)
        {
            ultimo = _motore.Handle(stato, new VoteCast(Giocatore(i), 0));
            stato = ultimo.State;
        }

        var finale = Assert.Single(ultimo.Broadcasts<GameFinishedMessage>());

        Assert.All(finale.Results, r => Assert.Equal(K, r.Authors.Count));
    }

    [Fact]
    public void VotareDueVolteVieneRifiutatoSoloAChiCiProva()
    {
        var stato = AlVoto();
        stato = _motore.Handle(stato, new VoteCast(Giocatore(0), 1)).State;

        var risultato = _motore.Handle(stato, new VoteCast(Giocatore(0), 2));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("ALREADY_VOTED", errore.Code);
        Assert.Equal(1, risultato.State.Votes[Giocatore(0)]);
        Assert.Empty(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
    }

    [Fact]
    public void UnIndiceInesistenteVieneRifiutato()
    {
        var stato = AlVoto();

        var risultato = _motore.Handle(stato, new VoteCast(Giocatore(0), 99));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NO_SUCH_PHRASE", errore.Code);
        Assert.Empty(risultato.State.Votes);
    }

    [Fact]
    public void VotareFuoriDallaFaseDiVotoVieneRifiutato()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "G0")).State;

        var risultato = _motore.Handle(stato, new VoteCast(Giocatore(0), 0));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NOT_VOTING", errore.Code);
    }

    [Fact]
    public void ChiNonEInStanzaNonPuoVotare()
    {
        var risultato = _motore.Handle(AlVoto(), new VoteCast(Giocatore(99), 0));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(99)));
        Assert.Equal("NOT_IN_ROOM", errore.Code);
    }

    [Fact]
    public void LHostChiudeIlVotoInAnticipoELaClassificaArrivaConLaFrasePiuVotata()
    {
        var stato = AlVoto();
        stato = _motore.Handle(stato, new VoteCast(Giocatore(0), 1)).State;

        var risultato = _motore.Handle(stato, new VotingCloseRequested(Giocatore(0)));

        Assert.Equal(RoomPhase.Finished, risultato.State.Phase);

        var finale = Assert.Single(risultato.Broadcasts<GameFinishedMessage>());
        Assert.Equal(N, finale.Results.Count);
        Assert.Equal(1, finale.Results[0].PhraseIndex);
        Assert.Equal(1, finale.Results[0].Votes);
        Assert.True(finale.Results[0].IsWinner);
    }

    [Fact]
    public void ChiNonEHostNonPuoChiudereIlVotoENonArrivaNessunaClassifica()
    {
        var stato = AlVoto();
        stato = _motore.Handle(stato, new VoteCast(Giocatore(0), 1)).State;

        var risultato = _motore.Handle(stato, new VotingCloseRequested(Giocatore(1)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("NOT_HOST", errore.Code);
        Assert.Empty(risultato.Broadcasts<GameFinishedMessage>());
        Assert.Equal(RoomPhase.Voting, risultato.State.Phase);
    }

    /// <summary>
    /// Distinto apposta dal caso "hanno vinto tutte a pari merito": qui
    /// nessuno ha votato, quindi nessuna frase vince, non tutte insieme.
    /// </summary>
    [Fact]
    public void LHostChiudeIlVotoSenzaCheNessunoAbbiaVotatoENessunaFraseVince()
    {
        var stato = AlVoto();

        var risultato = _motore.Handle(stato, new VotingCloseRequested(Giocatore(0)));

        Assert.Equal(RoomPhase.Finished, risultato.State.Phase);

        var finale = Assert.Single(risultato.Broadcasts<GameFinishedMessage>());
        Assert.Equal(N, finale.Results.Count);
        Assert.All(finale.Results, r => Assert.Equal(0, r.Votes));
        Assert.All(finale.Results, r => Assert.False(r.IsWinner));
    }

    /// <summary>
    /// Costruito interamente nel motore: se tutti gli umani si disconnettono
    /// durante il reveal, la successione dell'host (<c>OnPlayerLeft</c> in
    /// GameEngine.Players.cs) ripiega sull'host precedente quando non resta
    /// nessuno connesso — quindi l'host, benché disconnesso, può ancora far
    /// avanzare il reveal fino in fondo. Da lì si entra in voto con
    /// l'insieme dei votanti attesi vuoto: la stanza non deve restare
    /// appesa in <see cref="RoomPhase.Voting"/>.
    /// </summary>
    [Fact]
    public void SeTuttiIVotantiAttesiSiSonoDisconnessiIlVotoSiChiudeSubitoAllIngresso()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < N; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }

        Assert.Equal(RoomPhase.Reveal, stato.Phase);

        // Si disconnettono prima i non host, l'host per ultimo: finché non è
        // lui a lasciare la successione passa a chi resta connesso, ma
        // all'ultimo abbandono non resta nessuno e l'host di prima resta
        // host (ramo "?? state.HostId" in OnPlayerLeft).
        for (var i = N - 1; i >= 0; i--)
        {
            stato = _motore.Handle(stato, new PlayerLeft(Giocatore(i))).State;
        }

        Assert.Equal(Giocatore(0), stato.HostId);
        Assert.DoesNotContain(stato.Players, p => p.IsConnected);

        EngineResult ultimo = null!;
        for (var i = 0; i < N * K; i++)
        {
            ultimo = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
            stato = ultimo.State;
        }

        Assert.Equal(RoomPhase.Finished, ultimo.State.Phase);
        Assert.Single(ultimo.Broadcasts<GameFinishedMessage>());
    }

    /// <summary>
    /// Il caso che bloccherebbe la partita: se l'ultimo atteso se ne va, la
    /// chiusura deve avvenire nell'istante della disconnessione. Nessun altro
    /// evento arriverebbe a rivalutarla.
    /// </summary>
    [Fact]
    public void SeLUltimoAttesoSeNeVaIlVotoSiChiudeSubito()
    {
        var stato = AlVoto();
        stato = _motore.Handle(stato, new VoteCast(Giocatore(0), 1)).State;
        stato = _motore.Handle(stato, new VoteCast(Giocatore(1), 1)).State;

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(2)));

        Assert.Equal(RoomPhase.Finished, risultato.State.Phase);
        Assert.Single(risultato.Broadcasts<GameFinishedMessage>());
    }

    [Fact]
    public void SeSeNeVaUnoMaNonLUltimoIlVotoRestaAperto()
    {
        var stato = AlVoto();

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(2)));

        Assert.Equal(RoomPhase.Voting, risultato.State.Phase);
        Assert.Empty(risultato.Broadcasts<GameFinishedMessage>());
    }

    /// <summary>
    /// Chi ha votato e poi è caduto ha già detto la sua: la mappa è indicizzata
    /// per giocatore, non per connessione (spec §5).
    /// </summary>
    [Fact]
    public void IlVotoDiChiSiDisconnetteContaComunque()
    {
        var stato = AlVoto();
        stato = _motore.Handle(stato, new VoteCast(Giocatore(2), 1)).State;
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(2))).State;

        stato = _motore.Handle(stato, new VoteCast(Giocatore(0), 0)).State;
        var risultato = _motore.Handle(stato, new VoteCast(Giocatore(1), 0));

        var finale = Assert.Single(risultato.Broadcasts<GameFinishedMessage>());

        Assert.Equal(2, finale.Results.Single(r => r.PhraseIndex == 0).Votes);
        Assert.Equal(1, finale.Results.Single(r => r.PhraseIndex == 1).Votes);
    }
}
