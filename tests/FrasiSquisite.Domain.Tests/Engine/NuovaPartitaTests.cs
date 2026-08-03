using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

/// <summary>
/// Lotto D: uscire dalla schermata finale. Prima di questo lotto
/// <see cref="RoomPhase.Finished"/> era un vicolo cieco - nessun evento ne
/// usciva - quindi il test principale qui sotto è quello che dimostra il
/// difetto: due partite complete di fila nella stessa stanza.
/// </summary>
public class NuovaPartitaTests
{
    private const int N = 3;
    private const int K = 3;

    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static readonly IReadOnlyList<SchemaView> Schemi = [new SchemaView("test-3", "Test 3", K)];

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    private GameState StanzaConNGiocatori(int n) =>
        Enumerable.Range(0, n).Aggregate(
            GameState.NewRoom("ABCD", TestSchemas.WithSlots(K), Schemi),
            (stato, i) => _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State);

    /// <summary>
    /// Sottopone tutte le caselle di ogni round con un prefisso, porta il
    /// reveal fino in fondo e poi fa votare tutti: da questo lotto il reveal
    /// da solo non chiude più la partita (Task 4, fase di voto), e gli usi di
    /// questo aiutante presuppongono di arrivare fino a
    /// <see cref="RoomPhase.Finished"/>.
    /// </summary>
    private GameState GiocaFinoAlReveal(GameState stato, int n, string prefisso)
    {
        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < n; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"{prefisso}r{round}g{g}")).State;
            }
        }

        // Dalla fine della scrittura si passa per la rifinitura: nei test del
        // motore la si conclude senza modifiche, perche' il modello non c'e'.
        stato = _motore.Handle(stato, new RefinementFinished(null)).State;

        for (var i = 0; i < n * K; i++)
        {
            stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        }

        for (var g = 0; g < n; g++)
        {
            stato = _motore.Handle(stato, new VoteCast(Giocatore(g), 0)).State;
        }

        return stato;
    }

    private GameState PartitaFinita(int n, string prefisso = "p") =>
        GiocaFinoAlReveal(
            _motore.Handle(StanzaConNGiocatori(n), new GameStartRequested(Giocatore(0))).State,
            n,
            prefisso);

    // ================= Il test che dimostra il difetto =================

    [Fact]
    public void DuePartiteCompleteDiFilaNellaStessaStanza()
    {
        var stato = _motore.Handle(StanzaConNGiocatori(N), new GameStartRequested(Giocatore(0))).State;
        stato = GiocaFinoAlReveal(stato, N, "p1");

        Assert.Equal(RoomPhase.Finished, stato.Phase);
        Assert.Equal(N, stato.Phrases.Count);
        Assert.Contains(stato.Phrases, f => f.Slots.Any(s => s!.Text.StartsWith("p1", StringComparison.Ordinal)));

        // "Nuova partita": riparte subito, senza passare dalla lobby -
        // riusa la stessa strada di GameStartRequested (brief del lotto).
        var risultatoRipartenza = _motore.Handle(stato, new NewGameRequested(Giocatore(0)));
        stato = risultatoRipartenza.State;

        Assert.Equal(RoomPhase.Writing, stato.Phase);
        Assert.Equal(0, stato.Round);
        Assert.Equal(N, risultatoRipartenza.AllMessages().OfType<SlotRequestMessage>().Count());

        // Prima del fix questo secondo giro non era nemmeno raggiungibile:
        // Finished non lasciava uscire nessun evento.
        stato = GiocaFinoAlReveal(stato, N, "p2");

        Assert.Equal(RoomPhase.Finished, stato.Phase);
        Assert.Equal(N, stato.Phrases.Count);
        Assert.All(stato.Phrases, f => Assert.All(f.Slots, s => Assert.NotNull(s)));
        Assert.Contains(stato.Phrases, f => f.Slots.Any(s => s!.Text.StartsWith("p2", StringComparison.Ordinal)));
    }

    // ================= Guardie: solo in Finished, solo per l'host =================

    [Fact]
    public void NewGameRequestedFuoriDaFinishedDaNotFinished()
    {
        var stato = _motore.Handle(StanzaConNGiocatori(N), new GameStartRequested(Giocatore(0))).State;

        var risultato = _motore.Handle(stato, new NewGameRequested(Giocatore(0)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NOT_FINISHED", errore.Code);
        Assert.Equal(RoomPhase.Writing, risultato.State.Phase);
    }

    [Fact]
    public void BackToLobbyRequestedFuoriDaFinishedDaNotFinished()
    {
        var stato = _motore.Handle(StanzaConNGiocatori(N), new GameStartRequested(Giocatore(0))).State;

        var risultato = _motore.Handle(stato, new BackToLobbyRequested(Giocatore(0)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NOT_FINISHED", errore.Code);
        Assert.Equal(RoomPhase.Writing, risultato.State.Phase);
    }

    [Fact]
    public void SoloLHostPuoChiedereUnaNuovaPartita()
    {
        var stato = PartitaFinita(N);

        var risultato = _motore.Handle(stato, new NewGameRequested(Giocatore(1)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("NOT_HOST", errore.Code);
        Assert.Equal(RoomPhase.Finished, risultato.State.Phase);
    }

    [Fact]
    public void SoloLHostPuoTornareAllaLobby()
    {
        var stato = PartitaFinita(N);

        var risultato = _motore.Handle(stato, new BackToLobbyRequested(Giocatore(1)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("NOT_HOST", errore.Code);
        Assert.Equal(RoomPhase.Finished, risultato.State.Phase);
    }

    // ================= Azzeramento =================

    [Fact]
    public void BackToLobbyAzzeraLoStatoDiPartitaEConservaCodiceSchemaESchemiDisponibili()
    {
        var stato = PartitaFinita(N);

        var nuovo = _motore.Handle(stato, new BackToLobbyRequested(Giocatore(0))).State;

        Assert.Equal(RoomPhase.Lobby, nuovo.Phase);
        Assert.Equal(0, nuovo.Round);
        Assert.Empty(nuovo.Phrases);
        Assert.Empty(nuovo.SubmittedThisRound);
        Assert.Equal(0, nuovo.RevealPhraseIndex);
        Assert.Equal(0, nuovo.RevealSlotCount);
        Assert.Equal("ABCD", nuovo.RoomCode);
        Assert.Same(stato.Schema, nuovo.Schema);
        Assert.Same(stato.AvailableSchemas, nuovo.AvailableSchemas);
        Assert.Equal(stato.NextJoinOrder, nuovo.NextJoinOrder);
    }

    [Fact]
    public void UnUmanoDisconnessoVieneToltoUnBotNoUnUmanoConnessoNo()
    {
        var host = Giocatore(0);
        var umanoConnesso = Giocatore(1);
        var umanoDisconnesso = Giocatore(2);
        var botId = Giocatore(3);

        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K)) with
        {
            Phase = RoomPhase.Finished,
            HostId = host,
            Players =
            [
                new Player(host, "Host", IsBot: false, JoinOrder: 0, IsConnected: true),
                new Player(umanoConnesso, "Connesso", IsBot: false, JoinOrder: 1, IsConnected: true),
                new Player(umanoDisconnesso, "Disconnesso", IsBot: false, JoinOrder: 2, IsConnected: false),
                new Player(botId, "Bot Ada", IsBot: true, JoinOrder: 3, IsConnected: false),
            ],
        };

        var risultato = _motore.Handle(stato, new BackToLobbyRequested(host));
        var rimasti = risultato.State.Players.Select(p => p.Id).ToHashSet();

        Assert.Contains(host, rimasti);
        Assert.Contains(umanoConnesso, rimasti);
        Assert.Contains(botId, rimasti);
        Assert.DoesNotContain(umanoDisconnesso, rimasti);
    }

    [Fact]
    public void SeLHostEraFraIToltiIlRuoloPassaAUnConnesso()
    {
        var hostDisconnesso = Giocatore(0);
        var connesso = Giocatore(1);

        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K)) with
        {
            Phase = RoomPhase.Finished,
            HostId = hostDisconnesso,
            Players =
            [
                new Player(hostDisconnesso, "Host", IsBot: false, JoinOrder: 0, IsConnected: false),
                new Player(connesso, "Connesso", IsBot: false, JoinOrder: 1, IsConnected: true),
            ],
        };

        var risultato = _motore.Handle(stato, new BackToLobbyRequested(hostDisconnesso));

        Assert.Equal(connesso, risultato.State.HostId);
        Assert.DoesNotContain(risultato.State.Players, p => p.Id == hostDisconnesso);
    }

    // ================= NewGameRequested: sotto il minimo =================

    [Fact]
    public void NewGameRequestedSottoIlMinimoRestaInLobbyEMandaTooFewPlayersENonAvvia()
    {
        var host = Giocatore(0);
        var umanoDisconnesso = Giocatore(1);

        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K)) with
        {
            Phase = RoomPhase.Finished,
            HostId = host,
            Players =
            [
                new Player(host, "Host", IsBot: false, JoinOrder: 0, IsConnected: true),
                new Player(umanoDisconnesso, "Disconnesso", IsBot: false, JoinOrder: 1, IsConnected: false),
            ],
        };

        var risultato = _motore.Handle(stato, new NewGameRequested(host));

        Assert.Equal(RoomPhase.Lobby, risultato.State.Phase);
        Assert.Single(risultato.State.Players);
        Assert.Empty(risultato.MessagesTo<SlotRequestMessage>(host));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(host));
        Assert.Equal("TOO_FEW_PLAYERS", errore.Code);

        var statoTrasmesso = Assert.Single(risultato.Broadcasts<RoomStateMessage>());
        Assert.Equal(nameof(RoomPhase.Lobby), statoTrasmesso.Phase);
        Assert.Single(statoTrasmesso.Players);
    }

    // ================= NewGameRequested: abbastanza giocatori =================

    [Fact]
    public void NewGameRequestedConAbbastanzaGiocatoriArrivaInWritingConLeRichiesteDiCasellaGiaInviate()
    {
        var stato = PartitaFinita(N);

        var risultato = _motore.Handle(stato, new NewGameRequested(Giocatore(0)));

        Assert.Equal(RoomPhase.Writing, risultato.State.Phase);
        Assert.Equal(0, risultato.State.Round);
        for (var i = 0; i < N; i++)
        {
            Assert.Single(risultato.MessagesTo<SlotRequestMessage>(Giocatore(i)));
        }
    }

    // ================= BackToLobbyRequested: si può poi riavviare =================

    [Fact]
    public void BackToLobbyRequestedLasciaInLobbySenzaAvviareEDaLiSiPuoRiavviareComeSempre()
    {
        var stato = PartitaFinita(N);

        var dopoBackToLobby = _motore.Handle(stato, new BackToLobbyRequested(Giocatore(0))).State;

        Assert.Equal(RoomPhase.Lobby, dopoBackToLobby.Phase);
        Assert.Empty(dopoBackToLobby.Phrases);

        var risultatoAvvio = _motore.Handle(dopoBackToLobby, new GameStartRequested(Giocatore(0)));

        Assert.Equal(RoomPhase.Writing, risultatoAvvio.State.Phase);
        Assert.Equal(N, risultatoAvvio.AllMessages().OfType<SlotRequestMessage>().Count());
    }
}
