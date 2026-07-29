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
    private readonly IGameConnection _connection;
    private readonly Guid _playerId;

    public GameSessionViewModel(IGameConnection connection, Guid playerId)
    {
        _connection = connection;
        _playerId = playerId;

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

    [ObservableProperty]
    private bool _isHost;

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

    public ObservableCollection<PlayerView> Players { get; } = [];

    public ObservableCollection<string> RevealedSlots { get; } = [];

    public ObservableCollection<string> RevealAuthors { get; } = [];

    public ObservableCollection<string> FinalPhrases { get; } = [];

    [RelayCommand]
    private Task CreateRoomAsync() => EseguiComandoAsync(async () =>
    {
        ErrorText = string.Empty;
        await EnsureConnectedAsync();
        RoomCode = await _connection.CreateRoomAsync(_playerId, Nickname);
    });

    [RelayCommand]
    private Task JoinRoomAsync() => EseguiComandoAsync(async () =>
    {
        ErrorText = string.Empty;
        await EnsureConnectedAsync();
        RoomCode = JoinCode.Trim().ToUpperInvariant();
        await _connection.JoinRoomAsync(_playerId, Nickname, RoomCode);
    });

    [RelayCommand]
    private Task StartGameAsync() => EseguiComandoAsync(() => _connection.StartGameAsync(RoomCode));

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

    [RelayCommand]
    private Task AdvanceRevealAsync() => EseguiComandoAsync(() => _connection.AdvanceRevealAsync(RoomCode));

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
                    Players.Add(giocatore);
                }

                IsHost = stato.Players.Any(p => p.Id == _playerId && p.IsHost);
                PlayerCount = stato.Players.Count;

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
                RevealedSlots.Clear();
                foreach (var testo in passo.RevealedSlots)
                {
                    RevealedSlots.Add(testo);
                }

                RevealAuthors.Clear();
                foreach (var autore in passo.Authors)
                {
                    RevealAuthors.Add(autore);
                }

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
}
