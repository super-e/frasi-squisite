using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrasiSquisite.App.Services;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Validation;
using Microsoft.AspNetCore.SignalR;

namespace FrasiSquisite.App.ViewModels;

public enum ScreenState
{
    Home,
    Settings,
    Lobby,
    Writing,
    Waiting,
    Reveal,
    Finished,
}

/// <summary>
/// Una sola ViewModel per l'intera sessione: le schermate condividono lo stesso
/// stato e le stesse transizioni, e separarle costringerebbe a passarselo.
/// Non contiene logica di gioco: reagisce ai messaggi del server (spec §3.1).
/// </summary>
public partial class GameSessionViewModel : ObservableObject
{
    /// <summary>
    /// Duplica GameEngine.MaxPlayers (FrasiSquisite.Domain.Engine): l'App
    /// referenzia solo Shared, non Domain (che è motore per il server), quindi
    /// la costante non è raggiungibile da qui e va tenuta manualmente allineata
    /// all'originale (lotto-b-brief.md). Non un <c>cref</c> apposta: il progetto
    /// Domain non è nella closure di riferimenti dell'App, quindi non si
    /// risolverebbe.
    /// </summary>
    public const int MaxPlayers = 9;

    private readonly IGameConnection _connection;
    private readonly Guid _playerId;
    private readonly IThemeService _themeService;

    /// <summary>
    /// Autori della frase completata dall'ultimo passo di reveal, tenuti in
    /// disparte finché l'host non tocca di nuovo (battito "Chi l'ha scritta?"
    /// separato, vedi <see cref="AdvanceRevealAsync"/>). Il server li manda già
    /// insieme alla casella che completa la frase: nessun secondo giro dal
    /// server serve per mostrarli, quindi non è un [ObservableProperty].
    /// </summary>
    private IReadOnlyList<string> _autoriInAttesa = [];

    private bool _fraseCompleta;
    private bool _autoriMostratiPerQuestoPasso;

    public GameSessionViewModel(IGameConnection connection, Guid playerId, IThemeService themeService)
    {
        _connection = connection;
        _playerId = playerId;
        _themeService = themeService;

        _selectedTheme = themeService.Current;
        // Il tema può cambiare solo da Impostazioni, che passa sempre da
        // SelectThemeCommand qui sotto; questa sottoscrizione tiene comunque
        // SelectedTheme sincronizzato con la fonte di verità (IThemeService)
        // invece di duplicarne lo stato.
        _themeService.ThemeChanged += tema => SelectedTheme = tema;

        // Sottoscrizione nel costruttore: la ViewModel deve reagire ai messaggi
        // fin dal primo istante, anche prima che l'utente tocchi qualcosa.
        _connection.MessageReceived += OnMessage;
        _connection.ConnectionInterrupted += OnConnectionInterrupted;
    }

    [ObservableProperty]
    private ScreenState _screen = ScreenState.Home;

    [ObservableProperty]
    private string _serverUrl = "http://10.0.2.2:5000";

    [ObservableProperty]
    private string _nickname = string.Empty;

    [ObservableProperty]
    private string _roomCode = string.Empty;

    [ObservableProperty]
    private string _joinCode = string.Empty;

    /// <summary>
    /// Home: false mostra "Crea una stanza"/"Ho un codice", true li sostituisce
    /// con il campo del codice, "Entra" e "Indietro".
    /// </summary>
    [ObservableProperty]
    private bool _isJoiningByCode;

    [ObservableProperty]
    private bool _isHost;

    [ObservableProperty]
    private ThemeChoice _selectedTheme;

    /// <summary>
    /// Caselle dello schema per frase: arriva con <see cref="RoomStateMessage"/>
    /// (Domain, spec) e serve al reveal per sapere quante caselle "···" mostrare
    /// oltre a quelle già scoperte.
    /// </summary>
    [ObservableProperty]
    private int _slotCount;

    /// <summary>
    /// Arriva già con <see cref="RoomStateMessage"/> (spec) ma il client lo
    /// ignorava: la Lobby del design vuole il testo dello schema accanto al
    /// segnaposto del QR.
    /// </summary>
    [ObservableProperty]
    private string _schemaId = string.Empty;

    [ObservableProperty]
    private int _phraseNumber;

    [ObservableProperty]
    private int _totalPhrases;

    [ObservableProperty]
    private string _revealButtonLabel = "Rivela la prossima parola";

    [ObservableProperty]
    private string _ruolo = string.Empty;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _esempio = string.Empty;

    [ObservableProperty]
    private int _round;

    [ObservableProperty]
    private int _totalRounds;

    [ObservableProperty]
    private string _slotText = string.Empty;

    [ObservableProperty]
    private int _submittedCount;

    [ObservableProperty]
    private int _playerCount;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _connectionBanner = string.Empty;

    public ObservableCollection<PlayerRowView> Players { get; } = [];

    [ObservableProperty]
    private string _editingBotName = string.Empty;

    /// <summary>
    /// Vero solo se si è host, la fase è lobby e i giocatori sono meno di
    /// <see cref="MaxPlayers"/>: dal design, il pulsante "+ Aggiungi bot"
    /// scompare oltre quella soglia (lotto-b-brief.md).
    /// </summary>
    public bool CanAddBot => IsHost && Screen == ScreenState.Lobby && PlayerCount < MaxPlayers;

    /// <summary>
    /// Lunga sempre <see cref="SlotCount"/>: le caselle non ancora scoperte ci
    /// sono già, come segnaposto "···" (<see cref="RevealSlotView.IsRevealed"/>
    /// false), invece di apparire solo quando arrivano.
    /// </summary>
    public ObservableCollection<RevealSlotView> RevealSlots { get; } = [];

    /// <summary>
    /// Vuota finché l'host non tocca il pulsante nello stato "Chi l'ha
    /// scritta?": si popola da <see cref="_autoriInAttesa"/>, non da un nuovo
    /// messaggio del server.
    /// </summary>
    public ObservableCollection<string> RevealAuthors { get; } = [];

    public ObservableCollection<string> FinalPhrases { get; } = [];

    /// <summary>
    /// "Scritta da: A · B · C", vuota finché <see cref="RevealAuthors"/> non è
    /// popolata. Non è un [ObservableProperty] perché dipende da una
    /// collezione, non da un campo: la notifica parte da
    /// <see cref="MostraAutori"/>, l'unico punto che modifica RevealAuthors.
    /// </summary>
    public string AuthorsFootnote => RevealAuthors.Count == 0
        ? string.Empty
        : $"Scritta da: {string.Join(" · ", RevealAuthors)}";

    [RelayCommand]
    private Task CreateRoomAsync() => EseguiComandoAsync(async () =>
    {
        ErrorText = string.Empty;

        // Stesso validatore che il motore riapplica per BotRenamed: client e
        // server non possono divergere. Un nickname vuoto oggi produrrebbe
        // una riga bianca nella lobby di tutti (lotto-b-brief.md).
        var esito = NicknameValidator.Validate(Nickname);
        if (!esito.IsValid)
        {
            ErrorText = esito.Error!;
            return;
        }

        Nickname = esito.Normalized;
        await EnsureConnectedAsync();
        RoomCode = await _connection.CreateRoomAsync(_playerId, Nickname);
    });

    [RelayCommand]
    private Task JoinRoomAsync() => EseguiComandoAsync(async () =>
    {
        ErrorText = string.Empty;

        var esito = NicknameValidator.Validate(Nickname);
        if (!esito.IsValid)
        {
            ErrorText = esito.Error!;
            return;
        }

        Nickname = esito.Normalized;
        await EnsureConnectedAsync();
        RoomCode = JoinCode.Trim().ToUpperInvariant();
        await _connection.JoinRoomAsync(_playerId, Nickname, RoomCode);
    });

    [RelayCommand]
    private Task StartGameAsync() => EseguiComandoAsync(() => _connection.StartGameAsync(RoomCode));

    [RelayCommand]
    private Task AddBotAsync() => EseguiComandoAsync(() => _connection.AddBotAsync(RoomCode));

    [RelayCommand]
    private Task RemoveBotAsync(PlayerRowView riga) => EseguiComandoAsync(() => _connection.RemoveBotAsync(RoomCode, riga.Id));

    [RelayCommand]
    private void StartEditBot(PlayerRowView riga)
    {
        riga.IsEditing = true;
        EditingBotName = riga.Nickname;
    }

    [RelayCommand]
    private void CancelEditBot(PlayerRowView riga)
    {
        riga.IsEditing = false;
        EditingBotName = string.Empty;
    }

    [RelayCommand]
    private Task ConfirmEditBotAsync(PlayerRowView riga) => EseguiComandoAsync(async () =>
    {
        var esito = NicknameValidator.Validate(EditingBotName);
        if (!esito.IsValid)
        {
            ErrorText = esito.Error!;
            return;
        }

        await _connection.RenameBotAsync(RoomCode, riga.Id, esito.Normalized);
        riga.IsEditing = false;
        EditingBotName = string.Empty;
    });

    [RelayCommand]
    private void ShowJoinByCode() => IsJoiningByCode = true;

    [RelayCommand]
    private void HideJoinByCode()
    {
        IsJoiningByCode = false;
        JoinCode = string.Empty;
    }

    [RelayCommand]
    private void OpenSettings() => Screen = ScreenState.Settings;

    [RelayCommand]
    private void CloseSettings() => Screen = ScreenState.Home;

    [RelayCommand]
    private void SelectTheme(ThemeChoice tema) => _themeService.SetTheme(tema);

    [RelayCommand]
    private Task SubmitSlotAsync() => EseguiComandoAsync(async () =>
    {
        // Stesso validatore che riapplica il server: feedback immediato senza
        // che le due regole possano divergere.
        var esito = SlotTextValidator.Validate(SlotText);
        if (!esito.IsValid)
        {
            ErrorText = esito.Error!;
            return;
        }

        ErrorText = string.Empty;
        await _connection.SubmitSlotAsync(RoomCode, esito.Normalized);
        SlotText = string.Empty;
        Screen = ScreenState.Waiting;
    });

    /// <summary>
    /// Un solo comando per i tre stati del pulsante di reveal (spec del lotto):
    /// "Rivela la prossima parola" e "Prossima frase" chiamano il server,
    /// "Chi l'ha scritta?" no - mostra solo gli autori che il server ha già
    /// mandato nel passo che ha completato la frase (vedi <see cref="_autoriInAttesa"/>).
    /// Un'unica Command invece di tre evita alla view di dover scegliere quale
    /// invocare: la scelta segue lo stesso stato che decide l'etichetta.
    /// </summary>
    [RelayCommand]
    private Task AdvanceRevealAsync() => EseguiComandoAsync(async () =>
    {
        if (_fraseCompleta && !_autoriMostratiPerQuestoPasso)
        {
            MostraAutori(_autoriInAttesa);
            _autoriMostratiPerQuestoPasso = true;
            AggiornaEtichettaRevealButton();
            return;
        }

        await _connection.AdvanceRevealAsync(RoomCode);
    });

    private async Task EnsureConnectedAsync()
    {
        if (!_connection.IsConnected)
        {
            await _connection.ConnectAsync(ServerUrl);
        }
    }

    /// <summary>
    /// Ogni comando generato da [RelayCommand] diventa un AsyncRelayCommand: con
    /// le opzioni di default un'eccezione non osservata viene ri-lanciata sul
    /// thread che l'ha invocato, che su Android è il main looper - crash del
    /// processo per un errore di rete tutt'altro che eccezionale (URL sbagliato,
    /// stanza sparita, versione incompatibile, server irraggiungibile). Ogni
    /// comando passa quindi da qui: l'eccezione viene raccolta e mostrata
    /// nel banner invece di propagarsi.
    /// </summary>
    private async Task EseguiComandoAsync(Func<Task> azione)
    {
        try
        {
            await azione();
        }
        catch (HubException ex)
        {
            // Il server risponde già con un messaggio pensato per l'utente,
            // in italiano (es. "Stanza non trovata.", "...Aggiorna l'app."):
            // lo si mostra così com'è, senza riformularlo.
            ErrorText = ex.Message;
        }
        catch (Exception)
        {
            // Guasto di trasporto (URL irraggiungibile, connessione caduta
            // prima ancora di parlare con l'hub, ecc.): non c'è un messaggio
            // del server da mostrare, quindi uno generico ma comunque visibile
            // - non deve mai sparire nel nulla.
            ErrorText = "Non riesco a raggiungere il server.";
        }
    }

    private void OnConnectionInterrupted()
    {
        // Il trasporto SignalR può riconnettersi da solo (.WithAutomaticReconnect),
        // ma con un nuovo ConnectionId che non recupera l'appartenenza ai gruppi
        // della stanza: il server ha già marcato il giocatore disconnesso e ci
        // gioca un bot al suo posto. Il rejoin di partita è Fase 2 e resta fuori
        // scope, quindi l'avviso non si azzera da solo nemmeno se il trasporto
        // torna su (Reconnected): per questa sessione non cambia nulla.
        ConnectionBanner = "Connessione persa: un bot sta giocando al tuo posto.";
    }

    private void OnMessage(object message)
    {
        // Regola unica per lo svuotamento del banner d'errore: qualunque
        // messaggio diverso da ErrorMessage significa che il server è
        // andato avanti, quindi un errore mostrato in precedenza è ormai
        // stantio e va cancellato. Non basta agganciarsi al cambio di
        // schermata (OnScreenChanged) perché durante il Reveal ogni
        // RevealStepMessage reimposta Screen sullo stesso valore che ha
        // già: il setter generato da [ObservableProperty] non invoca
        // l'hook quando il valore non cambia, quindi un errore transitorio
        // (es. durante AdvanceReveal) resterebbe visibile anche dopo un
        // aggiornamento riuscito dello stesso schermo. Meglio un unico
        // punto qui che tanti "ErrorText = string.Empty" sparsi nei
        // singoli case dei messaggi.
        if (message is not ErrorMessage)
        {
            ErrorText = string.Empty;
        }

        switch (message)
        {
            case RoomStateMessage stato:
                RoomCode = stato.RoomCode;
                Players.Clear();
                foreach (var giocatore in stato.Players)
                {
                    Players.Add(new PlayerRowView(giocatore));
                }

                IsHost = stato.Players.Any(p => p.Id == _playerId && p.IsHost);
                PlayerCount = stato.Players.Count;
                SlotCount = stato.SlotCount;
                SchemaId = stato.SchemaId;

                // "Writing" NON va mappato qui, di proposito: una RoomStateMessage
                // arriva anche a partita in corso (es. qualcuno si disconnette), e
                // rimandare in scrittura chi ha già inviato lo strapperebbe dalla
                // schermata di attesa. "Reveal" invece è l'unico modo in cui un
                // client può uscire dall'attesa quando l'ultimo round finisce: il
                // motore non manda più SlotRequestMessage, quindi senza questo
                // ramo tutti resterebbero bloccati su "Aspettiamo gli altri…" (il
                // server manda comunque una RevealStepMessage iniziale apposta per
                // questo passaggio; qui è solo una difesa in profondità).
                if (stato.Phase == "Lobby")
                {
                    Screen = ScreenState.Lobby;
                }
                else if (stato.Phase == "Reveal")
                {
                    Screen = ScreenState.Reveal;
                }

                break;

            case SlotRequestMessage richiesta:
                Ruolo = richiesta.Ruolo;
                Prompt = richiesta.Prompt;
                Esempio = richiesta.Esempio;
                Round = richiesta.Round + 1;
                TotalRounds = richiesta.TotalRounds;
                SlotText = string.Empty;
                Screen = ScreenState.Writing;
                break;

            case RoundProgressMessage progresso:
                SubmittedCount = progresso.Submitted;
                PlayerCount = progresso.Total;
                break;

            case RevealStepMessage passo:
                PhraseNumber = passo.PhraseIndex + 1;
                TotalPhrases = passo.TotalPhrases;

                // SlotCount arriva con la RoomStateMessage che precede sempre
                // il reveal nel flusso reale; il fallback alle sole caselle
                // ricevute copre solo il caso (di solo test) in cui non sia
                // ancora nota, senza inventare segnaposto in più.
                var totaleCaselle = SlotCount > 0 ? SlotCount : passo.RevealedSlots.Count;
                RevealSlots.Clear();
                for (var i = 0; i < totaleCaselle; i++)
                {
                    RevealSlots.Add(i < passo.RevealedSlots.Count
                        ? new RevealSlotView(passo.RevealedSlots[i], true)
                        : new RevealSlotView("···", false));
                }

                // Gli autori di QUESTO passo restano in disparte: si mostrano
                // solo al tocco successivo (vedi AdvanceRevealAsync). Qui si
                // svuota anche la vista di quelli mostrati per il passo
                // precedente, altrimenti resterebbero appesi sotto la nuova frase.
                MostraAutori([]);
                _autoriInAttesa = passo.Authors;
                _fraseCompleta = passo.PhraseComplete;
                _autoriMostratiPerQuestoPasso = false;
                AggiornaEtichettaRevealButton();

                Screen = ScreenState.Reveal;
                break;

            case GameFinishedMessage finale:
                FinalPhrases.Clear();
                foreach (var frase in finale.Phrases)
                {
                    FinalPhrases.Add(frase);
                }

                Screen = ScreenState.Finished;
                break;

            case ErrorMessage errore:
                ErrorText = errore.Message;
                break;
        }
    }

    private void MostraAutori(IReadOnlyList<string> autori)
    {
        RevealAuthors.Clear();
        foreach (var autore in autori)
        {
            RevealAuthors.Add(autore);
        }

        // RevealAuthors è una ObservableCollection: la sua notifica non fa
        // scattare da sola quella di AuthorsFootnote, che ne dipende.
        OnPropertyChanged(nameof(AuthorsFootnote));
    }

    private void AggiornaEtichettaRevealButton()
    {
        RevealButtonLabel = !_fraseCompleta
            ? "Rivela la prossima parola"
            : _autoriMostratiPerQuestoPasso
                ? "Prossima frase"
                : "Chi l'ha scritta?";
    }

    // CanAddBot dipende da questi tre campi: nessuno dei tre notifica da solo
    // la proprietà calcolata, quindi lo fanno questi hook generati dal
    // toolkit per ogni [ObservableProperty] coinvolto.
    partial void OnIsHostChanged(bool value) => OnPropertyChanged(nameof(CanAddBot));

    partial void OnScreenChanged(ScreenState value) => OnPropertyChanged(nameof(CanAddBot));

    partial void OnPlayerCountChanged(int value) => OnPropertyChanged(nameof(CanAddBot));
}
