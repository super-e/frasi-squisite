using FrasiSquisite.App.Services;
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
        var vm = new GameSessionViewModel(connessione, Anna, new FakeThemeService(), new FakePlayerProfile()) { ServerUrl = "http://test" };
        return (vm, connessione);
    }

    private static (GameSessionViewModel Vm, FakeGameConnection Conn, FakeThemeService Tema) CreaConTema()
    {
        var connessione = new FakeGameConnection();
        var tema = new FakeThemeService();
        var vm = new GameSessionViewModel(connessione, Anna, tema, new FakePlayerProfile()) { ServerUrl = "http://test" };
        return (vm, connessione, tema);
    }

    private static (GameSessionViewModel Vm, FakeGameConnection Conn, FakePlayerProfile Profilo) CreaConProfilo(string nicknameSalvato = "")
    {
        var connessione = new FakeGameConnection();
        var profilo = new FakePlayerProfile { Nickname = nicknameSalvato };
        var vm = new GameSessionViewModel(connessione, Anna, new FakeThemeService(), profilo) { ServerUrl = "http://test" };
        return (vm, connessione, profilo);
    }

    /// <summary>
    /// Stanza nota e invito a votare già arrivato: il punto di partenza di
    /// tutti i test della fase di voto. <paramref name="ioSonoHost"/> decide
    /// se il giocatore locale è l'host, da cui dipende il pulsante di
    /// chiusura.
    /// </summary>
    private static (GameSessionViewModel Vm, FakeGameConnection Conn) InVoto(bool ioSonoHost = false)
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD",
            "Reveal",
            [new PlayerView(Anna, "Anna", ioSonoHost, true, false)],
            "storia",
            8));

        conn.Emit(new VoteRequestMessage(["Prima", "Seconda"]));

        return (vm, conn);
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
            [new PlayerView(Anna, "Anna", true, true, false), new PlayerView(Guid.NewGuid(), "Bruno", false, true, false)],
            "surrealista-classico", 5));

        Assert.Equal(ScreenState.Lobby, vm.Screen);
        Assert.Equal(2, vm.Players.Count);
        Assert.True(vm.IsHost);
        Assert.Equal("surrealista-classico", vm.SchemaId);
        Assert.Equal(5, vm.SlotCount);
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
            [new PlayerView(Anna, "Anna", true, true, false)],
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

    /// <summary>
    /// Con un bot in stanza l'invio dell'unico umano chiude il round, e
    /// GameHost attende l'invio di tutti gli effetti prima che il metodo hub
    /// ritorni: la richiesta di casella del round successivo arriva quindi
    /// PRIMA che l'await del client si sblocchi.
    ///
    /// Riportarsi in attesa dopo l'await cancellerebbe quello che il server ha
    /// gia' stabilito, e il blocco sarebbe definitivo: il server aspetta la
    /// casella del round nuovo, il client mostra l'attesa, e nessun altro
    /// messaggio arrivera' mai a sbloccarlo. E' il bug visto giocando, che
    /// nessun test poteva cogliere perche' con soli umani il proprio invio non
    /// chiude mai il round.
    /// </summary>
    [Fact]
    public async Task UnaRichiestaDiCasellaArrivataDuranteLInvioNonVieneSovrascritta()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new SlotRequestMessage(0, 5, "Soggetto", "prompt", "esempio"));
        vm.SlotText = "Il cadavere";

        conn.MessaggioDuranteInvio = new SlotRequestMessage(1, 5, "Aggettivo", "un aggettivo", "squisito");

        await vm.SubmitSlotCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Writing, vm.Screen);
        Assert.Equal(2, vm.Round);
        Assert.Equal("Aggettivo", vm.Ruolo);
    }

    /// <summary>
    /// La stessa collisione all'ultimo invio dell'ultimo round, dove il
    /// server passa al reveal. Qui non serve nessun bot: capita in qualunque
    /// partita a chi invia per ultimo, che senza questa protezione resterebbe
    /// in attesa mentre tutti gli altri guardano il reveal.
    /// </summary>
    [Fact]
    public async Task IlRevealArrivatoDuranteLInvioNonVieneSovrascritto()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new SlotRequestMessage(4, 5, "Aggettivo", "prompt", "esempio"));
        vm.SlotText = "nuovo";

        conn.MessaggioDuranteInvio = new RevealStepMessage(0, 2, ["Il cadavere"], false);

        await vm.SubmitSlotCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Reveal, vm.Screen);
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
    public void IlPassoDiRevealPopolaLeCaselleScoperteELascaCoperteLeRestanti()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Reveal",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 3));

        conn.Emit(new RevealStepMessage(0, 1, ["Il cadavere", "squisito"], false));

        Assert.Equal(ScreenState.Reveal, vm.Screen);
        Assert.Equal(3, vm.RevealSlots.Count);
        Assert.Equal(("Il cadavere", true), (vm.RevealSlots[0].Text, vm.RevealSlots[0].IsRevealed));
        Assert.Equal(("squisito", true), (vm.RevealSlots[1].Text, vm.RevealSlots[1].IsRevealed));
        Assert.Equal(("···", false), (vm.RevealSlots[2].Text, vm.RevealSlots[2].IsRevealed));
    }

    // Due stati, non più tre (voto cieco, spec §3): "Chi l'ha scritta?" è
    // sparito, gli autori arrivano solo con la classifica finale. Questo
    // test copre entrambe le cose che quello schiacciato copriva: le due
    // etichette del pulsante E che AdvanceRevealCommand arrivi sempre al
    // server, in entrambi gli stati - mai un ramo locale che si ferma
    // prima della chiamata (la regressione lato client che il voto cieco
    // doveva escludere).
    [Fact]
    public async Task IlPulsanteDiRevealMostraLetichettaGiustaEChiamaSempreIlServer()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";

        conn.Emit(new RevealStepMessage(0, 2, ["Il cadavere"], false));
        Assert.Equal("Rivela la prossima parola", vm.RevealButtonLabel);

        await vm.AdvanceRevealCommand.ExecuteAsync(null);
        Assert.Contains("AdvanceReveal(ABCD)", conn.Calls);

        conn.Emit(new RevealStepMessage(0, 2, ["Il cadavere", "squisito"], true));
        Assert.Equal("Prossima frase", vm.RevealButtonLabel);

        await vm.AdvanceRevealCommand.ExecuteAsync(null);
        Assert.Equal(2, conn.Calls.Count(c => c == "AdvanceReveal(ABCD)"));
    }

    [Fact]
    public void FraseNDiMRiflettePhraseIndexETotalPhrasesDelPasso()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RevealStepMessage(1, 3, ["berrà"], false));

        Assert.Equal(2, vm.PhraseNumber);
        Assert.Equal(3, vm.TotalPhrases);
    }

    [Fact]
    public void LaFinePartitaMostraLeFrasiComposte()
    {
        var (vm, conn) = Crea();

        conn.Emit(new GameFinishedMessage([
            new PhraseResultView(0, "Il cadavere squisito berrà il vino nuovo", [], 0, false),
        ]));

        Assert.Equal(ScreenState.Finished, vm.Screen);
        Assert.Single(vm.FinalResults);
    }

    [Fact]
    public void IlMessaggioFinalePopolaLaClassifica()
    {
        var (vm, conn) = Crea();

        conn.Emit(new GameFinishedMessage([
            new PhraseResultView(1, "Il notaio divora il tramonto", ["Anna", "Bruno"], 2, true),
            new PhraseResultView(0, "La zuppa scavalca una scala", ["Bruno", "Anna"], 0, false),
        ]));

        Assert.Equal(ScreenState.Finished, vm.Screen);
        Assert.Equal(2, vm.FinalResults.Count);
        Assert.True(vm.FinalResults[0].IsWinner);
        Assert.Equal("2 voti", vm.FinalResults[0].VotesLabel);
        Assert.Equal("Scritta da: Anna · Bruno", vm.FinalResults[0].AuthorsLabel);
        Assert.False(vm.FinalResults[1].IsWinner);
        Assert.Equal("0 voti", vm.FinalResults[1].VotesLabel);
    }

    [Fact]
    public void ConUnSoloVotoLEtichettaESingolare()
    {
        var (vm, conn) = Crea();

        conn.Emit(new GameFinishedMessage([
            new PhraseResultView(0, "Frase", ["Anna"], 1, true),
        ]));

        Assert.Equal("1 voto", vm.FinalResults[0].VotesLabel);
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
            [new PlayerView(Anna, "Anna", true, true, false)],
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
        conn.Emit(new RevealStepMessage(0, 3, ["Il cadavere"], false));
        Assert.Equal(ScreenState.Reveal, vm.Screen);

        conn.Emit(new ErrorMessage("TIMEOUT", "Richiesta scaduta, riprova."));
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));

        conn.Emit(new RevealStepMessage(0, 3, ["Il cadavere", "squisito"], false));

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
        vm.Nickname = "Bruno";
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

    // Home: "Ho un codice" sostituisce Crea/Ho-un-codice con CodeEntry, Entra,
    // Indietro (lotto-a-brief.md); "Indietro" torna allo stato iniziale e
    // pulisce quanto scritto, così non resta un codice a metà se si riapre.
    [Fact]
    public void HoUnCodiceMostraIlCampoDelCodiceEIndietroLoNasconde()
    {
        var (vm, _) = Crea();

        Assert.False(vm.IsJoiningByCode);

        vm.ShowJoinByCodeCommand.Execute(null);
        Assert.True(vm.IsJoiningByCode);

        vm.JoinCode = "ABCD";
        vm.HideJoinByCodeCommand.Execute(null);

        Assert.False(vm.IsJoiningByCode);
        Assert.Equal(string.Empty, vm.JoinCode);
    }

    [Fact]
    public void LIngranaggioApreImpostazioniEIndietroTornaAllaHome()
    {
        var (vm, _) = Crea();

        vm.OpenSettingsCommand.Execute(null);
        Assert.Equal(ScreenState.Settings, vm.Screen);

        vm.CloseSettingsCommand.Execute(null);
        Assert.Equal(ScreenState.Home, vm.Screen);
    }

    // Toccare una card del tema in Impostazioni deve cambiare tema subito
    // (lotto-a-brief.md: "toccarle cambia tema immediatamente"), passando dal
    // servizio - mai da uno stato locale che potrebbe disallinearsi da quello
    // davvero applicato alla UI.
    [Fact]
    public void SelezionareUnTemaLoInoltraAlServizioEAggiornaSelectedTheme()
    {
        var (vm, _, tema) = CreaConTema();

        vm.SelectThemeCommand.Execute(ThemeChoice.NotteDiGioco);

        Assert.Equal(ThemeChoice.NotteDiGioco, tema.Current);
        Assert.Equal([ThemeChoice.NotteDiGioco], tema.Impostati);
        Assert.Equal(ThemeChoice.NotteDiGioco, vm.SelectedTheme);
    }

    // ================= Bot (lotto-b-brief.md) =================

    [Fact]
    public async Task AggiungereUnBotChiamaLaConnessione()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";

        await vm.AddBotCommand.ExecuteAsync(null);

        Assert.Contains("AddBot(ABCD)", conn.Calls);
    }

    [Fact]
    public async Task RimuovereUnBotChiamaLaConnessioneConLIdDelBot()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        var botId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false), new PlayerView(botId, "Bot Ada", false, false, true)],
            "surrealista-classico", 5));
        var riga = vm.Players.Single(p => p.IsBot);

        await vm.RemoveBotCommand.ExecuteAsync(riga);

        Assert.Contains($"RemoveBot(ABCD,{botId})", conn.Calls);
    }

    [Fact]
    public void LoStatoDiModificaEntraEEsceConStartECancel()
    {
        var (vm, conn) = Crea();
        var botId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false), new PlayerView(botId, "Bot Ada", false, false, true)],
            "surrealista-classico", 5));
        var riga = vm.Players.Single(p => p.IsBot);

        vm.StartEditBotCommand.Execute(riga);
        Assert.True(riga.IsEditing);
        Assert.Equal("Bot Ada", vm.EditingBotName);

        vm.CancelEditBotCommand.Execute(riga);
        Assert.False(riga.IsEditing);
        Assert.Equal(string.Empty, vm.EditingBotName);
    }

    [Fact]
    public async Task ConfermareLaModificaChiamaLaConnessioneEEsceDallaModifica()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        var botId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false), new PlayerView(botId, "Bot Ada", false, false, true)],
            "surrealista-classico", 5));
        var riga = vm.Players.Single(p => p.IsBot);
        vm.StartEditBotCommand.Execute(riga);
        vm.EditingBotName = "Bot Nuovo";

        await vm.ConfirmEditBotCommand.ExecuteAsync(riga);

        Assert.Contains($"RenameBot(ABCD,{botId},Bot Nuovo)", conn.Calls);
        Assert.False(riga.IsEditing);
    }

    [Fact]
    public async Task ConfermareUnNomeNonValidoNonChiamaLaConnessione()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        var botId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false), new PlayerView(botId, "Bot Ada", false, false, true)],
            "surrealista-classico", 5));
        var riga = vm.Players.Single(p => p.IsBot);
        vm.StartEditBotCommand.Execute(riga);
        vm.EditingBotName = "   ";

        await vm.ConfirmEditBotCommand.ExecuteAsync(riga);

        Assert.DoesNotContain(conn.Calls, c => c.StartsWith("RenameBot", StringComparison.Ordinal));
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));
    }

    [Fact]
    public void CanAddBotEFalsoPerUnNonHost()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Guid.NewGuid(), "Anna", true, true, false), new PlayerView(Anna, "Bruno", false, true, false)],
            "surrealista-classico", 5));

        Assert.False(vm.IsHost);
        Assert.False(vm.CanAddBot);
    }

    [Fact]
    public void CanAddBotEFalsoAStanzaPiena()
    {
        var (vm, conn) = Crea();

        var giocatori = new List<PlayerView> { new(Anna, "Anna", true, true, false) };
        for (var i = 1; i < GameSessionViewModel.MaxPlayers; i++)
        {
            giocatori.Add(new PlayerView(Guid.NewGuid(), $"Bot {i}", false, false, true));
        }

        conn.Emit(new RoomStateMessage("ABCD", "Lobby", giocatori, "surrealista-classico", 5));

        Assert.True(vm.IsHost);
        Assert.Equal(GameSessionViewModel.MaxPlayers, vm.PlayerCount);
        Assert.False(vm.CanAddBot);
    }

    [Fact]
    public void CanAddBotEVeroPerHostInLobbyConPostoLibero()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 5));

        Assert.True(vm.CanAddBot);
    }

    // Punto 1 della revisione: con due bot in collezione, aprire la modifica
    // sul secondo deve chiudere quella (eventualmente) aperta sul primo -
    // EditingBotName è condiviso da tutte le righe, quindi due IsEditing
    // contemporaneamente farebbero disaccordare testo e riga. Nessun test
    // precedente aveva più di un bot, per questo il bug era passato inosservato.
    [Fact]
    public void ModificareUnSecondoBotChiudeLaModificaDelPrimo()
    {
        var (vm, conn) = Crea();
        var adaId = Guid.NewGuid();
        var brunoId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [
                new PlayerView(Anna, "Anna", true, true, false),
                new PlayerView(adaId, "Bot Ada", false, false, true),
                new PlayerView(brunoId, "Bot Bruno", false, false, true),
            ],
            "surrealista-classico", 5));
        var ada = vm.Players.Single(p => p.Id == adaId);
        var bruno = vm.Players.Single(p => p.Id == brunoId);

        vm.StartEditBotCommand.Execute(ada);
        Assert.True(ada.IsEditing);

        vm.StartEditBotCommand.Execute(bruno);

        Assert.False(ada.IsEditing);
        Assert.True(bruno.IsEditing);
        Assert.Equal("Bot Bruno", vm.EditingBotName);
    }

    // Riproduce esattamente lo scenario del punto 1: ✏ su Ada, poi ✏ su
    // Bruno, poi ✓ sulla riga di Ada. Prima della correzione questo
    // rinominava Ada in "Bot Bruno" (il testo digitato per l'altra riga).
    [Fact]
    public async Task ConfermareLaRigaChiusaDopoAverApertoUnAltraNonRinominaNulla()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        var adaId = Guid.NewGuid();
        var brunoId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [
                new PlayerView(Anna, "Anna", true, true, false),
                new PlayerView(adaId, "Bot Ada", false, false, true),
                new PlayerView(brunoId, "Bot Bruno", false, false, true),
            ],
            "surrealista-classico", 5));
        var ada = vm.Players.Single(p => p.Id == adaId);
        var bruno = vm.Players.Single(p => p.Id == brunoId);

        vm.StartEditBotCommand.Execute(ada);
        vm.StartEditBotCommand.Execute(bruno);

        await vm.ConfirmEditBotCommand.ExecuteAsync(ada);

        Assert.DoesNotContain(conn.Calls, c => c.StartsWith("RenameBot", StringComparison.Ordinal));
    }

    // Gemello simmetrico del test precedente, ma per l'annullamento: un tocco
    // su ✕ per Ada già in volo (accodato prima che la sua riga si chiudesse
    // visivamente) non deve corrompere la modifica tracciata di Bruno. Prima
    // della correzione CancelEditBot azzerava _editingBotId ed EditingBotName
    // incondizionatamente, lasciando la riga di Bruno con IsEditing true ma
    // testo vuoto e nessun id tracciato: confermarla avrebbe centrato la
    // guardia di ConfirmEditBotAsync (null != brunoId) e non avrebbe fatto
    // nulla, con la riga bloccata aperta senza alcun feedback.
    [Fact]
    public async Task AnnullareLaRigaChiusaDopoAverApertoUnAltraNonCorrompeLaModificaTracciata()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        var adaId = Guid.NewGuid();
        var brunoId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [
                new PlayerView(Anna, "Anna", true, true, false),
                new PlayerView(adaId, "Bot Ada", false, false, true),
                new PlayerView(brunoId, "Bot Bruno", false, false, true),
            ],
            "surrealista-classico", 5));
        var ada = vm.Players.Single(p => p.Id == adaId);
        var bruno = vm.Players.Single(p => p.Id == brunoId);

        vm.StartEditBotCommand.Execute(ada);
        vm.StartEditBotCommand.Execute(bruno);

        vm.CancelEditBotCommand.Execute(ada);

        Assert.True(bruno.IsEditing);
        Assert.Equal("Bot Bruno", vm.EditingBotName);

        await vm.ConfirmEditBotCommand.ExecuteAsync(bruno);

        Assert.Contains($"RenameBot(ABCD,{brunoId},Bot Bruno)", conn.Calls);
        Assert.False(bruno.IsEditing);
    }

    // Punto 2 della revisione: una RoomStateMessage (es. un altro giocatore
    // che entra) ricostruisce Players da zero. Senza preservare la modifica
    // per id, la riga tornerebbe non-editing e il testo digitato sparirebbe
    // dallo schermo alla prossima ricostruzione (StartEditBot lo
    // sovrascriverebbe con il nickname corrente).
    [Fact]
    public void UnaRoomStateMessageDuranteLaModificaDiUnBotNeConservaLoStato()
    {
        var (vm, conn) = Crea();
        var adaId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false), new PlayerView(adaId, "Bot Ada", false, false, true)],
            "surrealista-classico", 5));
        var ada = vm.Players.Single(p => p.Id == adaId);
        vm.StartEditBotCommand.Execute(ada);
        vm.EditingBotName = "Testo a metà";

        // Bruno entra: il server manda una nuova RoomStateMessage con tre
        // giocatori, che ricostruisce l'intera collezione Players.
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [
                new PlayerView(Anna, "Anna", true, true, false),
                new PlayerView(adaId, "Bot Ada", false, false, true),
                new PlayerView(Guid.NewGuid(), "Bruno", false, true, false),
            ],
            "surrealista-classico", 5));

        var rigaDopo = vm.Players.Single(p => p.Id == adaId);
        Assert.True(rigaDopo.IsEditing);
        Assert.Equal("Testo a metà", vm.EditingBotName);
    }

    // Punto 2, l'altro corno: se il bot in modifica sparisce (rimosso da un
    // altro client) non c'è più a chi riattaccare la modifica - va annullata
    // esplicitamente invece di lasciare il testo digitato appeso a un id morto.
    [Fact]
    public void UnaRoomStateMessageSenzaPiuIlBotInModificaAnnullaLaModifica()
    {
        var (vm, conn) = Crea();
        var adaId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false), new PlayerView(adaId, "Bot Ada", false, false, true)],
            "surrealista-classico", 5));
        var ada = vm.Players.Single(p => p.Id == adaId);
        vm.StartEditBotCommand.Execute(ada);
        vm.EditingBotName = "Testo a metà";

        // Ada è stata rimossa da un altro client: la nuova RoomStateMessage
        // non la contiene più.
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 5));

        Assert.Equal(string.Empty, vm.EditingBotName);
        Assert.DoesNotContain(vm.Players, p => p.IsEditing);
    }

    // Punto 3 della revisione: ShowBotControls era IsBot && !IsEditing, senza
    // gate sull'host - a differenza di CanAddBot, già gated. Un non-host che
    // guarda la lobby non deve vedere matita e ✕ sulle righe dei bot.
    [Fact]
    public void UnNonHostNonVedeIControlliDelBot()
    {
        var (vm, conn) = Crea();
        var hostId = Guid.NewGuid();
        var botId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [
                new PlayerView(hostId, "Anna", true, true, false),
                new PlayerView(Anna, "Bruno", false, true, false),
                new PlayerView(botId, "Bot Ada", false, false, true),
            ],
            "surrealista-classico", 5));

        Assert.False(vm.IsHost);
        var riga = vm.Players.Single(p => p.IsBot);
        Assert.False(riga.ShowBotControls);
    }

    [Fact]
    public void LHostVedeIControlliDelBot()
    {
        var (vm, conn) = Crea();
        var botId = Guid.NewGuid();
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [
                new PlayerView(Anna, "Anna", true, true, false),
                new PlayerView(botId, "Bot Ada", false, false, true),
            ],
            "surrealista-classico", 5));

        Assert.True(vm.IsHost);
        var riga = vm.Players.Single(p => p.IsBot);
        Assert.True(riga.ShowBotControls);
    }

    // ================= Schema (lotto-c-brief.md) =================

    [Fact]
    public void CanChangeSchemaEFalsoPerUnNonHost()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Guid.NewGuid(), "Anna", true, true, false), new PlayerView(Anna, "Bruno", false, true, false)],
            "surrealista-classico", 5,
            [new SchemaView("surrealista-classico", "Surrealista classico", 5)]));

        Assert.False(vm.IsHost);
        Assert.False(vm.CanChangeSchema);
    }

    [Fact]
    public void CanChangeSchemaEVeroPerHostInLobby()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 5,
            [new SchemaView("surrealista-classico", "Surrealista classico", 5)]));

        Assert.True(vm.IsHost);
        Assert.True(vm.CanChangeSchema);
    }

    [Fact]
    public async Task SceglierUnoSchemaChiamaLaConnessione()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";

        await vm.SelectSchemaCommand.ExecuteAsync("proverbio");

        Assert.Contains("SetSchema(ABCD,proverbio)", conn.Calls);
    }

    /// <summary>
    /// Schemi diversi hanno lunghezze diverse (lotto-c-brief.md): il
    /// conteggio mostrato deve seguire lo schema effettivamente selezionato
    /// nell'ultima RoomStateMessage, non un valore scritto a mano. "Mostrato"
    /// va inteso alla lettera: oltre a SlotCount (il dato) si verifica anche
    /// SchemaSummary (il testo che il non-host legge davvero), altrimenti il
    /// nome del test prometterebbe più di quanto le asserzioni controllino.
    /// </summary>
    [Fact]
    public void IlConteggioDelleCaselleSeguiLoSchemaSelezionato()
    {
        var (vm, conn) = Crea();
        var schemiDisponibili = new[]
        {
            new SchemaView("surrealista-classico", "Surrealista classico", 5),
            new SchemaView("proverbio", "Proverbio della nonna", 3),
        };

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 5, schemiDisponibili));
        Assert.Equal(5, vm.SlotCount);
        Assert.Equal("Surrealista classico — 5 caselle", vm.SchemaSummary);

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "proverbio", 3, schemiDisponibili));

        Assert.Equal(3, vm.SlotCount);
        Assert.Equal("proverbio", vm.SchemaId);
        Assert.Equal("Proverbio della nonna — 3 caselle", vm.SchemaSummary);
    }

    /// <summary>
    /// Lotto C (revisione): SchemaSummary non aveva alcuna copertura. Prima di
    /// ricevere una RoomStateMessage, SchemaNome è ancora la stringa vuota di
    /// default: il ramo vuoto di SchemaSummary deve restituire
    /// string.Empty, non "" — "N caselle" già a schermo prima di sapere
    /// quale schema è selezionato.
    /// </summary>
    [Fact]
    public void SchemaSummaryEVuotaPrimaDiRicevereUnaRoomStateMessage()
    {
        var (vm, _) = Crea();

        Assert.Equal(string.Empty, vm.SchemaSummary);
    }

    /// <summary>
    /// Il formato esatto "{Nome} — {N} caselle" che il design mostra a chi
    /// non è host, accanto al segnaposto del QR (lotto-c-brief.md).
    /// </summary>
    [Fact]
    public void SchemaSummaryComponeNomeTrattinoENumeroDiCaselle()
    {
        var (vm, conn) = Crea();
        var schemiDisponibili = new[] { new SchemaView("proverbio", "Proverbio della nonna", 3) };

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "proverbio", 3, schemiDisponibili));

        Assert.Equal("Proverbio della nonna — 3 caselle", vm.SchemaSummary);
    }

    /// <summary>
    /// SchemaNome e SlotCount notificano entrambi SchemaSummary (vedi
    /// OnSchemaNomeChanged/OnSlotCountChanged): senza quella notifica
    /// l'etichetta in lobby resterebbe quella dello schema precedente anche
    /// dopo che l'host ne ha scelto un altro, un bug che non romperebbe la
    /// build né farebbe fallire alcun test che guardi solo SchemaId/SlotCount.
    /// </summary>
    [Fact]
    public void CambiareLoSchemaNotificaSchemaSummary()
    {
        var (vm, conn) = Crea();
        var schemiDisponibili = new[]
        {
            new SchemaView("surrealista-classico", "Surrealista classico", 5),
            new SchemaView("proverbio", "Proverbio della nonna", 3),
        };
        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 5, schemiDisponibili));

        var proprietaNotificate = new List<string>();
        vm.PropertyChanged += (_, e) => proprietaNotificate.Add(e.PropertyName!);

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "proverbio", 3, schemiDisponibili));

        Assert.Contains(nameof(GameSessionViewModel.SchemaSummary), proprietaNotificate);
    }

    /// <summary>
    /// AvailableSchemas arriva vuoto in ogni fixture di test preesistente (e
    /// nella realtà finché la RoomStateMessage con il catalogo non è ancora
    /// arrivata): SchemaNome deve comunque ripiegare sull'id dello schema
    /// invece di restare vuoto, altrimenti SchemaSummary sparirebbe del tutto
    /// (vedi il fallback "?? stato.SchemaId" in GameSessionViewModel).
    /// </summary>
    [Fact]
    public void SenzaSchemiDisponibiliIlRiepilogoRipiegaSullId()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 5));

        Assert.Equal("surrealista-classico — 5 caselle", vm.SchemaSummary);
    }

    [Fact]
    public void LoSchemaSelezionatoEMarcatoComeTaleTraLeOpzioni()
    {
        var (vm, conn) = Crea();
        var schemiDisponibili = new[]
        {
            new SchemaView("surrealista-classico", "Surrealista classico", 5),
            new SchemaView("proverbio", "Proverbio della nonna", 3),
        };

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "proverbio", 3, schemiDisponibili));

        Assert.Equal(2, vm.SchemaOptions.Count);
        Assert.True(vm.SchemaOptions.Single(o => o.Id == "proverbio").IsSelected);
        Assert.False(vm.SchemaOptions.Single(o => o.Id == "surrealista-classico").IsSelected);
    }

    // ================= Validazione nickname lato client (punto 6) =================

    [Fact]
    public async Task CreareUnaStanzaConNicknameNonValidoVieneRifiutatoSenzaChiamareLaConnessione()
    {
        var (vm, conn) = Crea();
        vm.Nickname = "   ";

        await vm.CreateRoomCommand.ExecuteAsync(null);

        Assert.DoesNotContain(conn.Calls, c => c.StartsWith("CreateRoom", StringComparison.Ordinal));
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));
    }

    [Fact]
    public async Task CreareUnaStanzaNormalizzaIlNicknamePrimaDiInviarlo()
    {
        var (vm, conn) = Crea();
        vm.Nickname = "  Anna   Banana  ";

        await vm.CreateRoomCommand.ExecuteAsync(null);

        Assert.Equal("Anna Banana", vm.Nickname);
        Assert.Contains("CreateRoom(Anna Banana)", conn.Calls);
    }

    [Fact]
    public async Task EntrareInUnaStanzaConNicknameNonValidoVieneRifiutatoSenzaChiamareLaConnessione()
    {
        var (vm, conn) = Crea();
        vm.Nickname = "   ";
        vm.JoinCode = "ABCD";

        await vm.JoinRoomCommand.ExecuteAsync(null);

        Assert.DoesNotContain(conn.Calls, c => c.StartsWith("JoinRoom", StringComparison.Ordinal));
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));
    }

    // ================= Lotto D: "Nuova partita" / "Torna alla lobby" =================

    [Fact]
    public async Task NuovaPartitaChiamaLaConnessioneConIlCodiceStanza()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";

        await vm.NewGameCommand.ExecuteAsync(null);

        Assert.Contains("NewGame(ABCD)", conn.Calls);
    }

    [Fact]
    public async Task TornaAllaLobbyChiamaLaConnessioneConIlCodiceStanza()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";

        await vm.BackToLobbyCommand.ExecuteAsync(null);

        Assert.Contains("BackToLobby(ABCD)", conn.Calls);
    }

    [Fact]
    public void ShowFinishedActionsEVeroSoloPerLHostSullaSchermataFinale()
    {
        var (vm, conn) = Crea();

        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));
        Assert.Equal(ScreenState.Finished, vm.Screen);

        // Nessuna RoomStateMessage ancora emessa in questo test: IsHost resta
        // al default (false), come capiterebbe a chi non ha mai creato né
        // avviato la stanza.
        Assert.False(vm.ShowFinishedActions);
    }

    [Fact]
    public void ShowFinishedActionsEVeroPerLHostSullaSchermataFinale()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 5));
        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));

        Assert.True(vm.IsHost);
        Assert.Equal(ScreenState.Finished, vm.Screen);
        Assert.True(vm.ShowFinishedActions);
    }

    [Fact]
    public void ShowFinishedActionsEFalsoPerUnNonHostSullaSchermataFinale()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Guid.NewGuid(), "Anna", true, true, false), new PlayerView(Anna, "Bruno", false, true, false)],
            "surrealista-classico", 5));
        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));

        Assert.False(vm.IsHost);
        Assert.Equal(ScreenState.Finished, vm.Screen);
        Assert.False(vm.ShowFinishedActions);
    }

    [Fact]
    public void ShowFinishedActionsEFalsoPerLHostFuoriDallaSchermataFinale()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RoomStateMessage(
            "ABCD", "Lobby",
            [new PlayerView(Anna, "Anna", true, true, false)],
            "surrealista-classico", 5));

        Assert.True(vm.IsHost);
        Assert.Equal(ScreenState.Lobby, vm.Screen);
        Assert.False(vm.ShowFinishedActions);
    }

    // "Tornando in lobby le collezioni di partita risultano vuote" (brief del
    // lotto): senza questa pulizia la partita successiva mostrerebbe pezzi
    // di quella appena conclusa.
    [Fact]
    public async Task TornareAllaLobbySvuotaLeCollezioniDellaPartitaConclusa()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";

        conn.Emit(new RevealStepMessage(0, 1, ["Il cadavere"], true));
        await vm.AdvanceRevealCommand.ExecuteAsync(null);
        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));

        Assert.NotEmpty(vm.FinalResults);
        Assert.NotEmpty(vm.RevealSlots);

        await vm.BackToLobbyCommand.ExecuteAsync(null);

        Assert.Empty(vm.FinalResults);
        Assert.Empty(vm.RevealSlots);
    }

    // Stessa pulizia per "Nuova partita": la partita successiva parte dritta
    // alla scrittura, ma le collezioni della precedente non devono restare
    // appese in memoria nel frattempo.
    [Fact]
    public async Task NuovaPartitaSvuotaLeCollezioniDellaPartitaConclusa()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";

        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));
        Assert.NotEmpty(vm.FinalResults);

        await vm.NewGameCommand.ExecuteAsync(null);

        Assert.Empty(vm.FinalResults);
    }

    // ================= Lotto E (revisione del lotto D, punto 1) =================
    // PulisciStatoDiPartitaConclusa() veniva chiamato PRIMA dell'await sulla
    // connessione. Se il trasporto cade proprio in quel momento,
    // EseguiComandoAsync cattura il guasto e lo scrive in ErrorText, ma la
    // pulizia era già avvenuta: l'host restava sulla schermata finale con
    // l'errore sopra una lista di frasi ormai vuota, senza che nulla potesse
    // ripopolarla (le frasi finali arrivano solo con GameFinishedMessage, che
    // non tornerà). Questi due test dimostrano che, spostata la pulizia dopo
    // l'await, un guasto di trasporto lascia le frasi finali intatte.
    [Fact]
    public async Task UnGuastoDiTrasportoInNuovaPartitaNonCancellaLeFrasiFinali()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));
        Assert.NotEmpty(vm.FinalResults);
        conn.NextFailure = new HttpRequestException("host irraggiungibile");

        await vm.NewGameCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.FinalResults);
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));
    }

    [Fact]
    public async Task UnGuastoDiTrasportoInTornaAllaLobbyNonCancellaLeFrasiFinali()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";
        conn.Emit(new GameFinishedMessage([new PhraseResultView(0, "Il cadavere squisito", [], 0, false)]));
        Assert.NotEmpty(vm.FinalResults);
        conn.NextFailure = new HttpRequestException("host irraggiungibile");

        await vm.BackToLobbyCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.FinalResults);
        Assert.False(string.IsNullOrEmpty(vm.ErrorText));
    }

    [Fact]
    public async Task EntrareInUnaStanzaNormalizzaIlNicknamePrimaDiInviarlo()
    {
        var (vm, conn) = Crea();
        vm.Nickname = "  Bruno   Verdi  ";
        vm.JoinCode = "abcd";

        await vm.JoinRoomCommand.ExecuteAsync(null);

        Assert.Equal("Bruno Verdi", vm.Nickname);
        Assert.Contains("JoinRoom(Bruno Verdi,ABCD)", conn.Calls);
    }

    // ================= Lotto E, punto 3: il nickname si ricorda =================

    // Stesso schema di IThemeService: all'avvio la ViewModel legge subito il
    // valore persistito, senza aspettare un comando.
    [Fact]
    public void AllAvvioLaViewModelSiInizializzaColNicknameSalvato()
    {
        var (vm, _, _) = CreaConProfilo("Zia Norma");

        Assert.Equal("Zia Norma", vm.Nickname);
    }

    [Fact]
    public void SenzaUnNicknameSalvatoLaViewModelPartePartitaConNicknameVuoto()
    {
        var (vm, _) = Crea();

        Assert.Equal(string.Empty, vm.Nickname);
    }

    // Salvato solo dopo un uso riuscito (creazione stanza), e normalizzato -
    // non il testo grezzo scritto dall'utente.
    [Fact]
    public async Task CreareUnaStanzaSalvaIlNicknameNormalizzatoNelProfilo()
    {
        var (vm, _, profilo) = CreaConProfilo();
        vm.Nickname = "  Anna   Banana  ";

        await vm.CreateRoomCommand.ExecuteAsync(null);

        Assert.Equal("Anna Banana", profilo.Nickname);
    }

    [Fact]
    public async Task EntrareInUnaStanzaSalvaIlNicknameNormalizzatoNelProfilo()
    {
        var (vm, _, profilo) = CreaConProfilo();
        vm.Nickname = "  Bruno   Verdi  ";
        vm.JoinCode = "ABCD";

        await vm.JoinRoomCommand.ExecuteAsync(null);

        Assert.Equal("Bruno Verdi", profilo.Nickname);
    }

    [Fact]
    public async Task UnNicknameNonValidoNonVieneSalvatoNelProfilo()
    {
        var (vm, _, profilo) = CreaConProfilo();
        vm.Nickname = "   ";

        await vm.CreateRoomCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, profilo.Nickname);
        Assert.Empty(profilo.Salvati);
    }

    // Il caso più comune in assoluto: primo avvio con il server ancora
    // spento. Legare il salvataggio all'esito della rete farebbe perdere il
    // nickname proprio lì, cioè esattamente dove serve che venga ricordato.
    // "Usato" vuol dire confermato dall'utente, non raggiunto dal server.
    [Fact]
    public async Task UnGuastoDiTrasportoNonImpedisceDiRicordareIlNickname()
    {
        var (vm, conn, profilo) = CreaConProfilo();
        conn.NextFailure = new HttpRequestException("host irraggiungibile");
        vm.Nickname = "  anna  ";

        await vm.CreateRoomCommand.ExecuteAsync(null);

        Assert.Equal("anna", profilo.Nickname);
        Assert.NotEmpty(vm.ErrorText);
    }

    // ================= Lotto voto (task-8-brief.md) =================

    [Fact]
    public void LaRichiestaDiVotoPortaAllaSchermataDiVoto()
    {
        var (vm, conn) = Crea();

        conn.Emit(new VoteRequestMessage(["Prima frase", "Seconda frase"]));

        Assert.Equal(ScreenState.Voting, vm.Screen);
        Assert.Equal(2, vm.VoteOptions.Count);
        Assert.Equal("Prima frase", vm.VoteOptions[0].Text);
        Assert.Equal(1, vm.VoteOptions[1].Index);
        Assert.False(vm.HasVoted);
    }

    [Fact]
    public async Task VotareInviaLIndiceEPassaInAttesa()
    {
        var (vm, conn) = InVoto();

        await vm.CastVoteCommand.ExecuteAsync(vm.VoteOptions[1]);

        Assert.Contains("CastVote(ABCD,1)", conn.Calls);
        Assert.True(vm.HasVoted);
        Assert.Equal(ScreenState.Voting, vm.Screen);
    }

    /// <summary>
    /// L'ultimo votante riceve la classifica <em>durante</em> la propria
    /// await: GameHost invia tutti gli effetti prima che il metodo hub
    /// ritorni. Scrivere HasVoted dopo l'await senza guardia non basta —
    /// quello è innocuo — ma toccare Screen sì: lo riporterebbe sul voto a
    /// partita conclusa. È la forma esatta del bug che bloccò una partita
    /// vera su "in attesa 1 di 2".
    /// </summary>
    [Fact]
    public async Task SeLaClassificaArrivaDuranteIlVotoLaSchermataNonTornaIndietro()
    {
        var (vm, conn) = InVoto();

        conn.MessaggioDuranteVoto = new GameFinishedMessage([
            new PhraseResultView(0, "Prima", ["Anna"], 1, true),
        ]);

        await vm.CastVoteCommand.ExecuteAsync(vm.VoteOptions[0]);

        Assert.Equal(ScreenState.Finished, vm.Screen);
        Assert.Single(vm.FinalResults);
    }

    [Fact]
    public void LAvanzamentoDelVotoAggiornaIlConteggio()
    {
        var (vm, conn) = InVoto();

        conn.Emit(new VoteProgressMessage(1, 3));

        Assert.Equal(1, vm.VotedCount);
        Assert.Equal(3, vm.VotersExpected);
    }

    [Fact]
    public void ChiNonEHostNonVedeIlPulsantePerChiudereIlVoto()
    {
        var (vm, _) = InVoto(ioSonoHost: false);

        Assert.False(vm.ShowCloseVoting);
    }

    [Fact]
    public async Task LHostVedeIlPulsantePerChiudereIlVotoELoUsa()
    {
        var (vm, conn) = InVoto(ioSonoHost: true);

        Assert.True(vm.ShowCloseVoting);

        await vm.CloseVotingCommand.ExecuteAsync(null);

        Assert.Contains("CloseVoting(ABCD)", conn.Calls);
    }

    /// <summary>
    /// Una RoomStateMessage arriva anche a voto in corso (qualcuno si
    /// disconnette): non deve strappare dalla schermata d'attesa chi ha già
    /// votato, riportandolo sulla lista come se non avesse fatto nulla. È lo
    /// stesso motivo per cui "Writing" non viene mappato.
    /// </summary>
    [Fact]
    public async Task UnAggiornamentoDiStanzaNonRiportaAlVotoChiHaGiaVotato()
    {
        var (vm, conn) = InVoto();
        await vm.CastVoteCommand.ExecuteAsync(vm.VoteOptions[0]);

        conn.Emit(new RoomStateMessage(
            "ABCD",
            "Voting",
            [new PlayerView(Guid.NewGuid(), "Bruno", false, true, false)],
            "storia",
            8));

        Assert.True(vm.HasVoted);
    }

    /// <summary>
    /// Lacuna segnalata in revisione: i due agganci "messaggio durante" della
    /// connessione finta (<see cref="FakeGameConnection.MessaggioDuranteInvio"/>
    /// e <see cref="FakeGameConnection.MessaggioDuranteVoto"/>) sono due campi
    /// distinti proprio per non incrociarsi. Questo è il primo task in cui la
    /// ViewModel usa davvero entrambe le chiamate (SubmitSlot e CastVote), quindi
    /// è il primo punto in cui l'incrocio è verificabile: armare l'uno e
    /// invocare l'altro non deve emettere nulla.
    /// </summary>
    [Fact]
    public async Task IDueAgganciDiMessaggioDuranteNonSiIncrociano()
    {
        // Armare l'aggancio del voto e invocare l'invio di una casella non
        // deve emettere nulla: sono due proprietà distinte della connessione
        // finta apposta per questo.
        var (vmScrittura, connScrittura) = Crea();
        vmScrittura.RoomCode = "ABCD";
        connScrittura.Emit(new SlotRequestMessage(0, 5, "Soggetto", "prompt", "esempio"));
        vmScrittura.SlotText = "Il cadavere";
        connScrittura.MessaggioDuranteVoto = new VoteRequestMessage(["Prima", "Seconda"]);

        await vmScrittura.SubmitSlotCommand.ExecuteAsync(null);

        Assert.Equal(ScreenState.Waiting, vmScrittura.Screen);
        Assert.NotNull(connScrittura.MessaggioDuranteVoto);

        // E viceversa: armare l'aggancio dell'invio casella e votare non
        // deve emettere nulla.
        var (vmVoto, connVoto) = InVoto();
        connVoto.MessaggioDuranteInvio = new SlotRequestMessage(0, 5, "Soggetto", "prompt", "esempio");

        await vmVoto.CastVoteCommand.ExecuteAsync(vmVoto.VoteOptions[0]);

        Assert.Equal(ScreenState.Voting, vmVoto.Screen);
        Assert.NotNull(connVoto.MessaggioDuranteInvio);
    }
}
