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
        var vm = new GameSessionViewModel(connessione, Anna, new FakeThemeService()) { ServerUrl = "http://test" };
        return (vm, connessione);
    }

    private static (GameSessionViewModel Vm, FakeGameConnection Conn, FakeThemeService Tema) CreaConTema()
    {
        var connessione = new FakeGameConnection();
        var tema = new FakeThemeService();
        var vm = new GameSessionViewModel(connessione, Anna, tema) { ServerUrl = "http://test" };
        return (vm, connessione, tema);
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

        conn.Emit(new RevealStepMessage(0, 1, ["Il cadavere", "squisito"], false, []));

        Assert.Equal(ScreenState.Reveal, vm.Screen);
        Assert.Equal(3, vm.RevealSlots.Count);
        Assert.Equal(("Il cadavere", true), (vm.RevealSlots[0].Text, vm.RevealSlots[0].IsRevealed));
        Assert.Equal(("squisito", true), (vm.RevealSlots[1].Text, vm.RevealSlots[1].IsRevealed));
        Assert.Equal(("···", false), (vm.RevealSlots[2].Text, vm.RevealSlots[2].IsRevealed));
    }

    [Fact]
    public void FraseNDiMRiflettePhraseIndexETotalPhrasesDelPasso()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RevealStepMessage(1, 3, ["berrà"], false, []));

        Assert.Equal(2, vm.PhraseNumber);
        Assert.Equal(3, vm.TotalPhrases);
    }

    // Il server manda gli autori insieme alla casella che completa la frase,
    // ma la ViewModel li trattiene: mostrarli súbito brucerebbe il battito
    // "Chi l'ha scritta?" voluto dal design (lotto-a-brief.md).
    [Fact]
    public void GliAutoriRestanoNascostiFinchéNonSiTocaDiNuovoIlPulsante()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";

        conn.Emit(new RevealStepMessage(0, 2, ["Il cadavere", "squisito"], true, ["Anna", "Bruno"]));

        Assert.Empty(vm.RevealAuthors);
        Assert.Equal(string.Empty, vm.AuthorsFootnote);
    }

    // Le tre etichette del pulsante di reveal, nell'ordine in cui il design le
    // vuole: "Rivela la prossima parola" mentre restano caselle coperte, poi
    // "Chi l'ha scritta?" alla frase completa, poi "Prossima frase" dopo che
    // gli autori sono stati mostrati.
    [Fact]
    public async Task LEtichettaDelPulsanteDiRevealSeguiILTreStati()
    {
        var (vm, conn) = Crea();
        vm.RoomCode = "ABCD";

        conn.Emit(new RevealStepMessage(0, 2, ["Il cadavere"], false, []));
        Assert.Equal("Rivela la prossima parola", vm.RevealButtonLabel);

        conn.Emit(new RevealStepMessage(0, 2, ["Il cadavere", "squisito"], true, ["Anna", "Bruno"]));
        Assert.Equal("Chi l'ha scritta?", vm.RevealButtonLabel);
        Assert.Empty(vm.RevealAuthors);

        // Stato "Chi l'ha scritta?": tocco locale, nessuna chiamata al server.
        await vm.AdvanceRevealCommand.ExecuteAsync(null);
        Assert.Equal("Prossima frase", vm.RevealButtonLabel);
        Assert.Equal(["Anna", "Bruno"], vm.RevealAuthors);
        Assert.Equal("Scritta da: Anna · Bruno", vm.AuthorsFootnote);
        Assert.DoesNotContain(conn.Calls, c => c.StartsWith("AdvanceReveal", StringComparison.Ordinal));

        // Stato "Prossima frase": questo sì chiama il server.
        await vm.AdvanceRevealCommand.ExecuteAsync(null);
        Assert.Contains("AdvanceReveal(ABCD)", conn.Calls);
    }

    // Il passo successivo (nuova frase) deve ripulire gli autori mostrati per
    // quella precedente: altrimenti resterebbero appesi sotto la frase nuova.
    [Fact]
    public async Task UnNuovoPassoDiRevealNascondeGliAutoriDellaFrasePrecedente()
    {
        var (vm, conn) = Crea();

        conn.Emit(new RevealStepMessage(0, 2, ["Il cadavere"], true, ["Anna"]));
        await vm.AdvanceRevealCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.RevealAuthors);

        conn.Emit(new RevealStepMessage(1, 2, ["squisito"], false, []));

        Assert.Empty(vm.RevealAuthors);
        Assert.Equal(string.Empty, vm.AuthorsFootnote);
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
}
