using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrasiSquisite.App.Services;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Validation;

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

    public ObservableCollection<PlayerView> Players { get; } = [];

    public ObservableCollection<string> RevealedSlots { get; } = [];

    public ObservableCollection<string> RevealAuthors { get; } = [];

    public ObservableCollection<string> FinalPhrases { get; } = [];

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        ErrorText = string.Empty;
        await EnsureConnectedAsync();
        RoomCode = await _connection.CreateRoomAsync(_playerId, Nickname);
    }

    [RelayCommand]
    private async Task JoinRoomAsync()
    {
        ErrorText = string.Empty;
        await EnsureConnectedAsync();
        RoomCode = JoinCode.Trim().ToUpperInvariant();
        await _connection.JoinRoomAsync(_playerId, Nickname, RoomCode);
    }

    [RelayCommand]
    private Task StartGameAsync() => _connection.StartGameAsync(RoomCode);

    [RelayCommand]
    private async Task SubmitSlotAsync()
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
    }

    [RelayCommand]
    private Task AdvanceRevealAsync() => _connection.AdvanceRevealAsync(RoomCode);

    private async Task EnsureConnectedAsync()
    {
        if (!_connection.IsConnected)
        {
            await _connection.ConnectAsync(ServerUrl);
        }
    }

    private void OnMessage(object message)
    {
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

                if (stato.Phase == "Lobby")
                {
                    Screen = ScreenState.Lobby;
                }

                break;

            case SlotRequestMessage richiesta:
                Ruolo = richiesta.Ruolo;
                Prompt = richiesta.Prompt;
                Esempio = richiesta.Esempio;
                Round = richiesta.Round + 1;
                TotalRounds = richiesta.TotalRounds;
                SlotText = string.Empty;
                ErrorText = string.Empty;
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
