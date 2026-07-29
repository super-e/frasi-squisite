using FrasiSquisite.App.ViewModels;
using FrasiSquisite.Shared.Protocol;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace FrasiSquisite.App.Tests.ViewModels;

public class GameSessionViewModelTests
{
    private static readonly Guid Anna = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static (GameSessionViewModel Vm, FakeGameConnection Conn) Crea()
    {
        var connessione = new FakeGameConnection();
        var vm = new GameSessionViewModel(connessione, Anna) { ServerUrl = "http://test" };
        return (vm, connessione);
    }

    [Fact]
    public void AllAvvioSiEAllaSchermataIniziale()
    {
        var (vm, _) = Crea();

        Assert.Equal(ScreenState.Home, vm.Screen);
    }

    [Fact]
    public async Task CreareUnaStanzaChiamaLaConnessioneEMemorizzaIlCodice()
    {
        var (vm, conn) = Crea();
        conn.NextRoomCode = "WXYZ";
        vm.Nickname = "Anna";

        await vm.CreateRoomCommand.ExecuteAsync(null);

        Assert.Contains("CreateRoom(Anna)", conn.Calls);
        Assert.Equal("WXYZ", vm.RoomCode);
    }

    [Fact]
    public void RicevereLoStatoDellaStanzaPortaInLobbyEPopolaIGiocatori()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true), new PlayerView(Guid.NewGuid(), "Bruno", false, true)],
            "surrealista-classico", 5));

        Assert.Equal(ScreenState.Lobby, vm.Screen);
        Assert.Equal(2, vm.Players.Count);
        Assert.True(vm.IsHost);
    }

    // C1: senza il ramo "Reveal", una RoomStateMessage di fine partita non
    // sposta mai nessuno dalla schermata di attesa (il motore non manda più
    // SlotRequestMessage dopo l'ultimo round, e RevealStepMessage arriva solo
    // in risposta a un comando che solo l'host può inviare): il gioco resta
    // bloccato per sempre su "Aspettiamo gli altri…". "Writing" resta
    // deliberatamente fuori: una RoomStateMessage arriva anche a partita in
    // corso (es. qualcuno si disconnette) e rimapparla strapperebbe dalla
    // schermata di attesa chi ha già inviato per il round.
    [Theory]
    [InlineData("Lobby", ScreenState.Lobby)]
    [InlineData("Writing", ScreenState.Home)]
    [InlineData("Reveal", ScreenState.Reveal)]
    [InlineData("Finished", ScreenState.Home)]
    public void LoStatoDellaStanzaCambiaSchermataSoloPerLeFasiGestite(string fase, ScreenState atteso)
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", fase,
            [new PlayerView(Anna, "Anna", true, true)],
            "surrealista-classico", 5));

        Assert.Equal(atteso, vm.Screen);
    }

    [Fact]
    public void RicevereUnaRichiestaDiCasellaPortaInScritturaEMostraIlRuolo()
    {
        var (vm, conn) = Crea();

        conn.Emit(new SlotRequestMessage(0, 5, "Soggetto", "Un soggetto, con l'articolo", "Il cadavere"));

        Assert.Equal(ScreenState.Writing, vm.Screen);
        Assert.Equal("Soggetto", vm.Ruolo);
        Assert.Equal("Un soggetto, con l'articolo", vm.Prompt);
        Assert.Equal("Il cadavere", vm.Esempio);
        Assert.Equal(1, vm.Round);
        Assert.Equal(5, vm.TotalRounds);
    }

    [Fact]
    public async Task InviareUnaCasellaSvuotaIlCampoEPortaInAttesa()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new SlotRequestMessage(0, 5, "Soggetto", "prompt", "esempio"));
        vm.SlotText = "Il cadavere";

        await vm.SubmitSlotCommand.ExecuteAsync(null);

        Assert.Contains("SubmitSlot(ABCD,Il cadavere)", conn.Calls);
        Assert.Equal(string.Empty, vm.SlotText);
        Assert.Equal(ScreenState.Waiting, vm.Screen);
    }

    [Fact]
    public async Task NonSiPuoInviareUnTestoNonValido()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new SlotRequestMessage(0, 5, "Soggetto", "prompt", "esempio"));
        vm.SlotText = "   ";

        await vm.SubmitSlotCommand.ExecuteAsync(null);

        Assert.DoesNotContain(conn.Calls, c => c.StartsWith("SubmitSlot", StringComparison.Ordinal));
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));
    }

    [Fact]
    public void IlProgressoDelRoundAggiornaIlConteggio()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoundProgressMessage(0, 2, 4));

        Assert.Equal(2, vm.SubmittedCount);
        Assert.Equal(4, vm.PlayerCount);
    }

    [Fact]
    public void IlPassoDiRevealPopolaCaselleEAutori()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RevealStepMessage(0, 3, ["Il cadavere", "squisito"], false, []));

        Assert.Equal(ScreenState.Reveal, vm.Screen);
        Assert.Equal(2, vm.RevealedSlots.Count);
        Assert.Empty(vm.RevealAuthors);

        conn.Emit(new RevealStepMessage(0, 3, ["Il cadavere", "squisito", "berrà"], true, ["Anna", "Bruno", "Carla"]));

        Assert.Equal(3, vm.RevealAuthors.Count);
    }

    [Fact]
    public void LaFinePartitaMostraLeFrasiComposte()
    {
        var (vm, conn) = Crea();

        conn.Emit(new GameFinishedMessage(["Il cadavere squisito berrà il vino nuovo"]));

        Assert.Equal(ScreenState.Finished, vm.Screen);
        Assert.Single(vm.FinalPhrases);
    }

    [Fact]
    public void UnErroreDalServerVieneMostrato()
    {
        var (vm, conn) = Crea();

        conn.Emit(new ErrorMessage("NOT_HOST", "Solo chi ha creato la stanza può avviare."));

        Assert.Equal("Solo chi ha creato la stanza può avviare.", vm.ErrorText);
    }

    [Fact]
    public void UnErroreNonSopravviveAlCambioDiSchermata()
    {
        var (vm, conn) = Crea();

        conn.Emit(new ErrorMessage("NOT_HOST", "Solo chi ha creato la stanza può avviare."));
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true)],
            "surrealista-classico", 5));

        Assert.Equal(ScreenState.Lobby, vm.Screen);
        Assert.Equal(string.Empty, vm.ErrorText);
    }

    [Fact]
    public void UnErroreNonSopravviveAUnMessaggioSullaStessaSchermata()
    {
        var (vm, conn) = Crea();

        // Si arriva sulla schermata di Reveal e ci si resta: RevealStepMessage
        // imposta Screen sullo stesso valore che ha già, quindi il setter
        // generato non invoca OnScreenChanged. La regola di pulizia
        // dell'errore non può quindi dipendere dal cambio di schermata.
        conn.Emit(new RevealStepMessage(0, 3, ["Il cadavere"], false, []));
        Assert.Equal(ScreenState.Reveal, vm.Screen);

        conn.Emit(new ErrorMessage("TIMEOUT", "Richiesta scaduta, riprova."));
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));

        conn.Emit(new RevealStepMessage(0, 3, ["Il cadavere", "squisito"], false, []));

        Assert.Equal(ScreenState.Reveal, vm.Screen);
        Assert.Equal(string.Empty, vm.ErrorText);
    }

    [Fact]
    public void UnErroreNonVieneCancellatoDalProprioArrivo()
    {
        var (vm, conn) = Crea();

        conn.Emit(new ErrorMessage("NOT_HOST", "Solo chi ha creato la stanza può avviare."));

        Assert.Equal("Solo chi ha creato la stanza può avviare.", vm.ErrorText);
    }

    // C2: [RelayCommand] genera un AsyncRelayCommand le cui opzioni di
    // default non fanno fluire l'eccezione allo scheduler: un'azione utente
    // di tutti i giorni (URL sbagliato, stanza sparita, server riavviato)
    // altrimenti farebbe esplodere il processo. Il fatto stesso che
    // ExecuteAsync qui sotto non lanci è la prova che l'eccezione non si
    // propaga più.
    [Fact]
    public async Task UnGuastoDiTrasportoNellaCreazioneDellaStanzaVieneMostratoENonPropaga()
    {
        var (vm, conn) = Crea();
        conn.NextFailure = new HttpRequestException("host irraggiungibile");
        vm.Nickname = "Anna";

        await vm.CreateRoomCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.ErrorText));
        Assert.NotEqual("host irraggiungibile", vm.ErrorText);
    }

    [Fact]
    public async Task UnHubExceptionMostraIlMessaggioDelServerVerbatim()
    {
        var (vm, conn) = Crea();
        conn.NextFailure = new HubException("Stanza non trovata.");
        vm.JoinCode = "ABCD";

        await vm.JoinRoomCommand.ExecuteAsync(null);

        Assert.Equal("Stanza non trovata.", vm.ErrorText);
    }

    [Fact]
    public async Task UnGuastoNellInvioDellaCasellaVieneMostratoENonPortaInAttesa()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new SlotRequestMessage(0, 5, "Soggetto", "prompt", "esempio"));
        vm.SlotText = "Il cadavere";
        conn.NextFailure = new HubException("Non è il momento di scrivere.");

        await vm.SubmitSlotCommand.ExecuteAsync(null);

        Assert.Equal("Non è il momento di scrivere.", vm.ErrorText);
        Assert.Equal(ScreenState.Writing, vm.Screen);
    }

    // I1: un riavvio del trasporto (.WithAutomaticReconnect) apre una
    // connessione con un nuovo ConnectionId che non recupera l'appartenenza
    // ai gruppi SignalR della stanza: da quel momento un bot gioca al posto
    // del giocatore, ma senza questo avviso lo schermo non lo direbbe mai
    // (IsConnected resterebbe true, nessun messaggio in arrivo lo segnala).
    [Fact]
    public void UnInterruzioneDiConnessioneMostraUnAvviso()
    {
        var (vm, conn) = Crea();

        Assert.Equal(string.Empty, vm.ConnectionBanner);

        conn.EmitConnectionInterrupted();

        Assert.False(string.IsNullOrEmpty(vm.ConnectionBanner));
    }
}
