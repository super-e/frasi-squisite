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
    Refining,
    Reveal,
    Voting,
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
    private readonly IPlayerProfile _playerProfile;
    private readonly IRoomSession _roomSession;

    private bool _fraseCompleta;

    public GameSessionViewModel(
        IGameConnection connection,
        Guid playerId,
        IThemeService themeService,
        IPlayerProfile playerProfile,
        IRoomSession roomSession)
    {
        _connection = connection;
        _playerId = playerId;
        _themeService = themeService;
        _playerProfile = playerProfile;
        _roomSession = roomSession;

        _selectedTheme = themeService.Current;
        // Il tema può cambiare solo da Impostazioni, che passa sempre da
        // SelectThemeCommand qui sotto; questa sottoscrizione tiene comunque
        // SelectedTheme sincronizzato con la fonte di verità (IThemeService)
        // invece di duplicarne lo stato.
        _themeService.ThemeChanged += tema => SelectedTheme = tema;

        // Stesso schema del tema: il nickname salvato (lotto-e-brief.md) va
        // letto subito, non solo quando l'utente tocca qualcosa. Se non c'è
        // nulla di salvato, IPlayerProfile.Nickname è string.Empty - lo
        // stesso valore di default del campo, quindi resta vuoto come oggi.
        _nickname = playerProfile.Nickname;

        // Sottoscrizione nel costruttore: la ViewModel deve reagire ai messaggi
        // fin dal primo istante, anche prima che l'utente tocchi qualcosa.
        _connection.MessageReceived += OnMessage;
        _connection.ConnectionInterrupted += OnConnectionInterrupted;
        _connection.Reconnected += OnReconnected;
    }

    /// <summary>
    /// Tenta un rientro silenzioso nella stanza salvata (design rientro
    /// §5.2): usa RoomCode se già popolato (si crede già in una stanza — il
    /// caso del trasporto appena tornato), altrimenti quello persistito da
    /// IRoomSession (il caso dell'avvio a freddo, dove RoomCode è ancora
    /// vuoto). Nessun banner d'errore su un fallimento: dal punto di vista
    /// dell'utente non è un errore, è solo "quella partita non c'è più" - il
    /// fallimento silenzioso copre anche un guasto di puro trasporto (app
    /// offline all'avvio), per restare quanto più possibile senza attrito.
    /// </summary>
    public async Task TryRejoinAsync()
    {
        var codice = RoomCode.Length > 0 ? RoomCode : _roomSession.RoomCode;
        if (codice.Length == 0)
        {
            return;
        }

        try
        {
            await EnsureConnectedAsync();
            await _connection.RejoinRoomAsync(_playerId, codice);
        }
        catch
        {
            // Un guasto qui è di trasporto puro (server irraggiungibile,
            // offline all'avvio): non significa "quella partita non c'è
            // più" - solo un vero RejoinRejectedMessage lo dice (gestito
            // nel case sotto, in OnMessage). Cancellare la sessione qui
            // perderebbe la partita salvata proprio nel caso più comune
            // che questo metodo esiste per proteggere: rete lenta o
            // assente al primo avvio (rilievo Critical 2 della revisione
            // finale).
        }
    }

    private void OnReconnected() => _ = TryRejoinAsync();

    [ObservableProperty]
    private ScreenState _screen = ScreenState.Home;

    /// <summary>
    /// Il server pubblico del gioco. Resta modificabile per poter puntare a
    /// un'istanza locale durante lo sviluppo; quando quell'esigenza cadrà, il
    /// campo in Impostazioni sparisce e questo diventa l'unico indirizzo
    /// possibile.
    /// </summary>
    public const string DefaultServerUrl = "https://frasisquisite.carraraenri.co/";

    [ObservableProperty]
    private string _serverUrl = DefaultServerUrl;

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

    /// <summary>
    /// Nome per esteso dello schema selezionato, cercato in
    /// <see cref="SchemaOptions"/> quando arriva la RoomStateMessage. Serve
    /// al riepilogo mostrato a chi non è host (<see cref="SchemaSummary"/>):
    /// mostrare solo <see cref="SchemaId"/> ("surrealista-classico") non è
    /// il testo del design ("Surrealista classico").
    /// </summary>
    [ObservableProperty]
    private string _schemaNome = string.Empty;

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

    /// <summary>
    /// Gli schemi che il server offre, arrivati con l'ultima
    /// <see cref="RoomStateMessage"/> (spec del lotto). Come per i bot, il
    /// selettore che li mostra è visibile solo all'host
    /// (<see cref="CanChangeSchema"/>): gli altri vedono solo
    /// <see cref="SchemaSummary"/>.
    /// </summary>
    public ObservableCollection<SchemaOptionView> SchemaOptions { get; } = [];

    [ObservableProperty]
    private string _editingBotName = string.Empty;

    /// <summary>
    /// Id del bot la cui riga ha attualmente l'Entry aperta, o null se
    /// nessuna. <see cref="EditingBotName"/> da solo non basta a sapere QUALE
    /// riga sta modificando: due righe potrebbero entrambe avere
    /// <c>IsEditing</c> a true (bug corretto qui) e comunque servirebbe
    /// sapere quale sopravvive a una ricostruzione di <see cref="Players"/>
    /// (RoomStateMessage). Questo campo è la fonte di verità per entrambe le
    /// cose.
    /// </summary>
    private Guid? _editingBotId;

    /// <summary>
    /// Vero solo se si è host, la fase è lobby e i giocatori sono meno di
    /// <see cref="MaxPlayers"/>: dal design, il pulsante "+ Aggiungi bot"
    /// scompare oltre quella soglia (lotto-b-brief.md).
    /// </summary>
    public bool CanAddBot => IsHost && Screen == ScreenState.Lobby && PlayerCount < MaxPlayers;

    /// <summary>
    /// Solo l'host vede il selettore: chi lo tocca senza esserlo riceverebbe
    /// solo un NOT_HOST dal motore invece di non vedere affatto il
    /// controllo (stessa regola di <see cref="CanAddBot"/> e di
    /// <see cref="PlayerRowView.ShowBotControls"/> per i bot).
    /// </summary>
    public bool CanChangeSchema => IsHost && Screen == ScreenState.Lobby;

    /// <summary>
    /// "Nuova partita" e "Torna alla lobby" (lotto-d-brief.md): come per
    /// "Inizia partita" e l'avanzamento del reveal, solo l'host li vede - un
    /// pulsante che risponde NOT_HOST al tocco è peggio di un pulsante
    /// assente. Chi non è host legge un avviso discreto al suo posto.
    /// </summary>
    public bool ShowFinishedActions => IsHost && Screen == ScreenState.Finished;

    /// <summary>
    /// "Nome — N caselle", quello che il design mostra a chi non è host
    /// accanto al segnaposto del QR. Il conteggio non è scritto a mano:
    /// segue sempre <see cref="SlotCount"/>, che arriva dallo schema
    /// effettivamente selezionato (schemi diversi hanno lunghezze diverse,
    /// lotto-c-brief.md).
    /// </summary>
    public string SchemaSummary => SchemaNome.Length == 0
        ? string.Empty
        : $"{SchemaNome} — {SlotCount} caselle";

    /// <summary>
    /// Riflette 1:1 i frammenti mandati dal server per il passo di reveal
    /// corrente: testo fisso del template e caselle, scoperte o coperte con
    /// "···" (<see cref="RevealFragmentView.IsRevealed"/> false). Il
    /// riempimento delle caselle non ancora arrivate lo fa già il server:
    /// qui non c'è più bisogno di completare la lista con <c>SlotCount</c>
    /// (piano docs/superpowers/plans/2026-08-06-reveal-fluido.md).
    /// </summary>
    public ObservableCollection<RevealFragmentView> RevealFragments { get; } = [];

    public ObservableCollection<PhraseResultRowView> FinalResults { get; } = [];

    /// <summary>
    /// L'illustrazione ingrandita a schermo intero, o null se nessuna è
    /// aperta. Un solo overlay alla volta: non serve stato per riga.
    /// </summary>
    [ObservableProperty]
    private string? _expandedImageUrl;

    [ObservableProperty]
    private bool _hasVoted;

    [ObservableProperty]
    private int _votedCount;

    [ObservableProperty]
    private int _votersExpected;

    public ObservableCollection<VoteOptionView> VoteOptions { get; } = [];

    /// <summary>
    /// Come per "Inizia partita" e l'avanzamento del reveal: chi non è host
    /// non vede il pulsante, invece di vederselo rispondere NOT_HOST.
    /// </summary>
    public bool ShowCloseVoting => IsHost && Screen == ScreenState.Voting;

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

        // Salvato qui, prima di toccare la rete: "usato" vuol dire che
        // l'utente l'ha confermato premendo Crea, non che il server era
        // raggiungibile. Legarlo all'esito della connessione lo farebbe
        // perdere proprio nel caso più comune - primo avvio, server ancora
        // spento - che è esattamente quello che questa funzione esiste per
        // evitare.
        _playerProfile.SaveNickname(Nickname);

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

        // Stesso motivo di CreateRoomAsync: si salva alla conferma, non
        // all'esito della rete.
        _playerProfile.SaveNickname(Nickname);

        // Come in CreateRoomAsync: RoomCode va assegnato solo DOPO che la
        // rete conferma, non prima. Se il tentativo iniziale fallisce a
        // trasporto freddo (nessuna HubConnection ancora aperta) e viene
        // ritentato da ReconnectTransportAndRoomAsync, quel metodo decide se
        // rientrare guardando proprio RoomCode: assegnarlo qui prima
        // dell'esito lo farebbe scambiare per "già in una stanza" e
        // scatenerebbe un RejoinRoom spurio per una stanza in cui non si è
        // mai davvero entrati, invece di lasciare che il retry richiami
        // questo stesso JoinRoomAsync (rilievo Important della revisione,
        // design 2026-08-13 "retry di riconnessione").
        var codice = JoinCode.Trim().ToUpperInvariant();
        await _connection.JoinRoomAsync(_playerId, Nickname, codice);
        RoomCode = codice;
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
        // Solo una riga alla volta può essere in modifica: EditingBotName è
        // un'unica proprietà condivisa da tutte le Entry (vedi GamePage.xaml),
        // quindi due righe entrambe IsEditing finirebbero per mostrare - ed
        // eventualmente confermare - lo stesso testo sulla riga sbagliata.
        // Toccare la matita su una riga chiude quindi qualunque altra fosse
        // aperta.
        foreach (var altra in Players)
        {
            if (altra.IsEditing && altra != riga)
            {
                altra.IsEditing = false;
            }
        }

        riga.IsEditing = true;
        EditingBotName = riga.Nickname;
        _editingBotId = riga.Id;
    }

    [RelayCommand]
    private void CancelEditBot(PlayerRowView riga)
    {
        if (_editingBotId != riga.Id)
        {
            // La riga toccata non è (più) quella in modifica: stesso caso
            // della guardia in ConfirmEditBotAsync, un ✕ già in volo su una
            // riga che StartEditBot ha nel frattempo chiuso. Ignorare invece
            // di azzerare _editingBotId/EditingBotName, che corromperebbe la
            // riga effettivamente tracciata (rimarrebbe IsEditing == true ma
            // senza id né testo, bloccata aperta).
            return;
        }

        riga.IsEditing = false;
        EditingBotName = string.Empty;
        _editingBotId = null;
    }

    [RelayCommand]
    private Task ConfirmEditBotAsync(PlayerRowView riga) => EseguiComandoAsync(async () =>
    {
        if (_editingBotId != riga.Id)
        {
            // La riga toccata non è (più) quella in modifica: capita se
            // StartEditBot ne ha aperta un'altra nel frattempo (che chiude
            // sempre la precedente) o se Players è stato ricostruito senza
            // che questa riga fosse quella preservata. Ignorare invece di
            // rinominarla con il testo digitato per un'altra riga (punto 1
            // della revisione: EditingBotName era condiviso da tutte).
            return;
        }

        var esito = NicknameValidator.Validate(EditingBotName);
        if (!esito.IsValid)
        {
            ErrorText = esito.Error!;
            return;
        }

        await _connection.RenameBotAsync(RoomCode, riga.Id, esito.Normalized);
        riga.IsEditing = false;
        EditingBotName = string.Empty;
        _editingBotId = null;
    });

    [RelayCommand]
    private Task SelectSchemaAsync(string schemaId) => EseguiComandoAsync(() => _connection.SetSchemaAsync(RoomCode, schemaId));

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

        // GameHost esegue il motore e attende l'invio di TUTTI gli effetti
        // prima che il metodo hub ritorni: se il nostro invio chiude il round,
        // la richiesta di casella successiva (o il reveal) arriva prima che
        // questo await si sblocchi, e il gestore dei messaggi ha gia' portato
        // la schermata dove deve stare.
        //
        // Succede a ogni round quando in stanza c'e' un bot - il nostro e'
        // l'unico invio mancante - e comunque all'ultimo invio dell'ultimo
        // round in qualsiasi partita, perche' li' si passa al reveal.
        //
        // Percio' l'attesa si imposta solo se nel frattempo non e' arrivato
        // nulla: sovrascrivere lo stato deciso dal server lascerebbe il client
        // fermo per sempre, con il server che aspetta una casella e il
        // giocatore che guarda una schermata d'attesa che nessun messaggio
        // sbloccherebbe piu'.
        var roundInviato = Round;

        await _connection.SubmitSlotAsync(RoomCode, esito.Normalized);
        SlotText = string.Empty;

        if (Screen == ScreenState.Writing && Round == roundInviato)
        {
            Screen = ScreenState.Waiting;
        }
    });

    /// <summary>
    /// Due stati, non più tre: con il voto cieco (spec §3) il passo "Chi
    /// l'ha scritta?" non ha più niente da mostrare — gli autori arrivano
    /// solo alla fine, con la classifica.
    /// </summary>
    [RelayCommand]
    private Task AdvanceRevealAsync() => EseguiComandoAsync(() => _connection.AdvanceRevealAsync(RoomCode));

    /// <summary>
    /// GameHost invia tutti gli effetti prima che il metodo hub ritorni,
    /// quindi se il nostro è l'ultimo voto la classifica arriva mentre siamo
    /// ancora dentro l'await, e il gestore dei messaggi ha già portato la
    /// schermata su Finished. Il pericolo che la guardia previene è scrivere
    /// stato della schermata di voto dopo che la fase è già finita: qui, a
    /// differenza di <see cref="SubmitSlotAsync"/>, l'unica scrittura dopo
    /// l'await è <see cref="HasVoted"/>, che a partita conclusa non legge più
    /// nessuno - oggi la guardia è quindi innocua, non indispensabile. Resta
    /// comunque perché è il punto esatto in cui un'aggiunta futura (per
    /// esempio una scrittura di <see cref="Screen"/> dopo l'await, per
    /// rispecchiare <see cref="SubmitSlotAsync"/>) reintrodurrebbe il difetto
    /// vero: trovarci già la guardia è ciò che lo previene.
    /// </summary>
    [RelayCommand]
    private Task CastVoteAsync(VoteOptionView opzione) => EseguiComandoAsync(async () =>
    {
        ErrorText = string.Empty;

        await _connection.CastVoteAsync(RoomCode, opzione.Index);

        if (Screen == ScreenState.Voting)
        {
            HasVoted = true;
        }
    });

    [RelayCommand]
    private Task CloseVotingAsync() => EseguiComandoAsync(() => _connection.CloseVotingAsync(RoomCode));

    [RelayCommand]
    private Task RequestIllustrationAsync(PhraseResultRowView riga) => EseguiComandoAsync(async () =>
    {
        // L'attesa si accende PRIMA dell'await: è ciò che impedisce il secondo
        // tocco mentre il primo è in volo. Il motore rifiuterebbe comunque il
        // doppione, ma con un errore a schermo invece che con niente.
        riga.IsWaiting = true;

        try
        {
            await _connection.RequestIllustrationAsync(RoomCode, riga.PhraseIndex);
        }
        catch
        {
            // Se non è nemmeno partita, l'attesa non deve restare accesa: non
            // arriverà nessun messaggio a spegnerla. Il guasto lo racconta
            // EseguiComandoAsync, che rilancia.
            riga.IsWaiting = false;
            throw;
        }
    });

    /// <summary>
    /// Apre l'illustrazione a schermo intero. Nessuna chiamata al server:
    /// l'indirizzo è già noto al client (spec 2026-08-08).
    /// </summary>
    [RelayCommand]
    private void ExpandImage(string url) => ExpandedImageUrl = url;

    /// <summary>
    /// Chiude l'illustrazione ingrandita. Tocco ovunque sull'overlay, non
    /// solo su un pulsante di chiusura dedicato.
    /// </summary>
    [RelayCommand]
    private void CollapseImage() => ExpandedImageUrl = null;

    /// <summary>
    /// "Nuova partita" (lotto-d-brief.md): riparte subito, stessi giocatori e
    /// stesso schema. Ripulisce lo stato della partita appena conclusa DOPO
    /// l'await, come <see cref="BackToLobbyAsync"/> (lotto-e-brief.md, punto 1
    /// della revisione): se il trasporto cade proprio in quel momento,
    /// EseguiComandoAsync cattura il guasto e lo scrive in ErrorText, e la
    /// pulizia semplicemente non avviene - le frasi finali restano a schermo
    /// invece di sparire sotto un errore senza che nulla possa più
    /// ripopolarle (arrivano solo con GameFinishedMessage, che non tornerà).
    /// Sicuro nell'altro verso: né RoomStateMessage né SlotRequestMessage
    /// toccano quelle collezioni, quindi non c'è nulla che possa ripopolarle
    /// nel frattempo.
    /// </summary>
    [RelayCommand]
    private Task NewGameAsync() => EseguiComandoAsync(async () =>
    {
        await _connection.NewGameAsync(RoomCode);
        PulisciStatoDiPartitaConclusa();
    });

    /// <summary>
    /// "Torna alla lobby" (lotto-d-brief.md): nessun avvio, solo ripulitura
    /// dello stato della partita appena conclusa. Pulizia dopo l'await per lo
    /// stesso motivo di <see cref="NewGameAsync"/>.
    /// </summary>
    [RelayCommand]
    private Task BackToLobbyAsync() => EseguiComandoAsync(async () =>
    {
        await _connection.BackToLobbyAsync(RoomCode);
        PulisciStatoDiPartitaConclusa();
    });

    private async Task EnsureConnectedAsync()
    {
        if (!_connection.IsConnected)
        {
            await _connection.ConnectAsync(ServerUrl);
        }
    }

    /// <summary>
    /// Riconnette il trasporto e, se si crede già in una stanza, rientra
    /// anche lì (design 2026-08-13 "retry di riconnessione", §3.1). A
    /// differenza di <see cref="TryRejoinAsync"/> non inghiotte le
    /// eccezioni: chi lo chiama (il retry in <see cref="EseguiComandoAsync"/>
    /// o <see cref="ReconnectAsync"/>) decide come mostrarle.
    /// </summary>
    private async Task ReconnectTransportAndRoomAsync()
    {
        await EnsureConnectedAsync();

        if (RoomCode.Length > 0)
        {
            await _connection.RejoinRoomAsync(_playerId, RoomCode);
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
            // lo si mostra così com'è, senza riformularlo. Un rifiuto del
            // server non è un guasto di trasporto: ritentarlo otterrebbe
            // solo lo stesso rifiuto, quindi non passa dal retry sotto.
            ErrorText = ex.Message;
        }
        catch (Exception)
        {
            // Guasto di trasporto (URL irraggiungibile, connessione caduta
            // prima ancora di parlare con l'hub, ecc.): un solo tentativo di
            // riconnessione + rientro, poi si ripete l'azione una volta sola
            // (design 2026-08-13 "retry di riconnessione", §3.2). Nessun
            // backoff, nessun retry-del-retry: se fallisce ancora, stesso
            // messaggio generico di prima - l'utente può riprovare lui
            // stesso (azione o bottone "Riconnetti").
            try
            {
                await ReconnectTransportAndRoomAsync();
                await azione();
            }
            catch (HubException ex)
            {
                ErrorText = ex.Message;
            }
            catch (Exception)
            {
                ErrorText = "Non riesco a raggiungere il server.";
            }
        }
    }

    /// <summary>
    /// Un solo tentativo per pressione, senza passare da EseguiComandoAsync:
    /// quel wrapper ritenterebbe chiamando ReconnectTransportAndRoomAsync una
    /// seconda volta al suo interno - dato che qui è sia l'azione sia il
    /// meccanismo di recupero, il risultato sarebbe un secondo RejoinRoomAsync
    /// duplicato verso il server a una singola pressione (design 2026-08-13
    /// "retry di riconnessione", §3.3: "nessun retry-del-retry interno").
    /// </summary>
    [RelayCommand]
    private async Task ReconnectAsync()
    {
        try
        {
            await ReconnectTransportAndRoomAsync();
        }
        catch (HubException ex)
        {
            ErrorText = ex.Message;
        }
        catch (Exception)
        {
            ErrorText = "Non riesco a raggiungere il server.";
        }
    }

    private void OnConnectionInterrupted()
    {
        // Il trasporto è giù o ci sta provando (Reconnecting/Closed): il
        // giocatore potrebbe restare temporaneamente sostituito da un bot
        // finché non rientra per davvero. Reconnected (sopra, OnReconnected)
        // è separato apposta: solo lì ha senso tentare un rientro, non qui.
        ConnectionBanner = "Connessione persa: un bot sta giocando al tuo posto.";
    }

    private void OnMessage(object message)
    {
        // Qualunque messaggio dal server (incluso un RoomStateMessage dopo
        // un rientro riuscito) è prova che il giro di andata e ritorno
        // funziona di nuovo: sia l'errore sia l'avviso di connessione
        // instabile sono ormai stantii (design 2026-08-13 "retry di
        // riconnessione", §3.4). Questa pulizia vive qui e non in un hook
        // OnScreenChanged perché durante il Reveal ogni RevealStepMessage
        // riassegna Screen allo stesso valore che ha già: il setter generato
        // da [ObservableProperty] non invoca On<Prop>Changed quando il
        // valore nuovo è uguale al vecchio, quindi un hook non pulirebbe mai
        // un errore o un banner rimasti stantii durante il Reveal.
        if (message is not ErrorMessage)
        {
            ErrorText = string.Empty;
            ConnectionBanner = string.Empty;
        }

        switch (message)
        {
            case RoomStateMessage stato:
                RoomCode = stato.RoomCode;
                _roomSession.Save(stato.RoomCode);

                // Calcolato prima di ricostruire le righe: ShowBotControls di
                // ogni riga dipende da chi guarda, non da chi è quella riga
                // (vedi PlayerRowView.ViewerIsHost), quindi serve pronto per
                // il costruttore invece che assegnato dopo.
                var eroHost = stato.Players.Any(p => p.Id == _playerId && p.IsHost);

                Players.Clear();
                foreach (var giocatore in stato.Players)
                {
                    var riga = new PlayerRowView(giocatore, eroHost);

                    // La ricostruzione delle righe (ogni RoomStateMessage ne
                    // crea di nuove) perderebbe altrimenti la modifica in
                    // corso di un bot - ad es. un altro giocatore che si
                    // unisce mentre l'host sta rinominando un bot
                    // cancellerebbe silenziosamente il testo digitato.
                    // EditingBotName non viene toccato qui: sopravvive già
                    // da solo, serve solo far ricomparire il campo su questa
                    // nuova riga.
                    if (_editingBotId == giocatore.Id && giocatore.IsBot)
                    {
                        riga.IsEditing = true;
                    }

                    Players.Add(riga);
                }

                // Il bot in modifica non esiste più in questo stato (rimosso
                // da un altro client, o non è più un bot): niente a cui
                // riattaccare la modifica, quindi la si annulla esplicitamente
                // invece di lasciare il testo digitato appeso a un id morto.
                if (_editingBotId is { } idInModifica
                    && !stato.Players.Any(p => p.Id == idInModifica && p.IsBot))
                {
                    _editingBotId = null;
                    EditingBotName = string.Empty;
                }

                IsHost = eroHost;
                PlayerCount = stato.Players.Count;
                SlotCount = stato.SlotCount;
                SchemaId = stato.SchemaId;

                SchemaOptions.Clear();
                foreach (var schema in stato.AvailableSchemas)
                {
                    SchemaOptions.Add(new SchemaOptionView(schema, schema.Id == stato.SchemaId));
                }

                // Se lo schema selezionato non compare nell'elenco (es. in
                // test che non popolano AvailableSchemas), il riepilogo
                // ripiega sull'id invece di restare vuoto: SchemaSummary
                // controlla comunque SchemaNome.Length, quindi meglio un
                // valore comunque leggibile che "Lunghezza zero" silenzioso.
                SchemaNome = stato.AvailableSchemas.FirstOrDefault(s => s.Id == stato.SchemaId)?.Nome
                    ?? stato.SchemaId;

                // "Writing" NON va mappato qui, di proposito: una RoomStateMessage
                // arriva anche a partita in corso (es. qualcuno si disconnette), e
                // rimandare in scrittura chi ha già inviato lo strapperebbe dalla
                // schermata di attesa. "Reveal" invece è l'unico modo in cui un
                // client può uscire dall'attesa quando l'ultimo round finisce: il
                // motore non manda più SlotRequestMessage, quindi senza questo
                // ramo tutti resterebbero bloccati su "Aspettiamo gli altri…" (il
                // server manda comunque una RevealStepMessage iniziale apposta per
                // questo passaggio; qui è solo una difesa in profondità).
                //
                // "Voting" NON va mappato, per lo stesso motivo di "Writing":
                // una RoomStateMessage arriva anche a voto in corso (qualcuno
                // si disconnette), e riporterebbe sulla lista chi ha già
                // votato. Sulla schermata di voto ci si arriva con
                // VoteRequestMessage, che è per definizione l'invito a votare.
                if (stato.Phase == "Lobby")
                {
                    Screen = ScreenState.Lobby;
                }
                else if (stato.Phase == "Reveal")
                {
                    Screen = ScreenState.Reveal;
                }
                // "Refining" SI mappa, a differenza di "Writing" e "Voting":
                // durante la rifinitura nessuno ha niente da fare e tutti
                // devono andare sulla schermata di passaggio, quindi qui la
                // mappatura e' esattamente cio' che serve.
                else if (stato.Phase == "Refining")
                {
                    Screen = ScreenState.Refining;
                }

                break;

            case SlotRequestMessage richiesta:
                Round = richiesta.Round + 1;
                TotalRounds = richiesta.TotalRounds;

                // Un rientro dopo che il periodo di grazia è scaduto trova
                // la propria casella del round corrente già riempita da un
                // bot (rilievo Important 3 della revisione finale):
                // mostrare comunque un campo editabile porterebbe a un
                // invio respinto con ALREADY_SUBMITTED. Si mostra l'attesa,
                // come dopo un invio proprio riuscito.
                if (richiesta.GiaInviato)
                {
                    Screen = ScreenState.Waiting;
                    break;
                }

                Ruolo = richiesta.Ruolo;
                Prompt = richiesta.Prompt;
                Esempio = richiesta.Esempio;
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

                RevealFragments.Clear();
                foreach (var frammento in passo.Fragments)
                {
                    RevealFragments.Add(frammento.IsSlot
                        ? new RevealFragmentView(true, frammento.IsRevealed ? frammento.Text : "···", frammento.IsRevealed)
                        : new RevealFragmentView(false, frammento.Text, true));
                }

                _fraseCompleta = passo.PhraseComplete;
                AggiornaEtichettaRevealButton();

                Screen = ScreenState.Reveal;
                break;

            case VoteRequestMessage daVotare:
                VoteOptions.Clear();
                for (var i = 0; i < daVotare.Phrases.Count; i++)
                {
                    VoteOptions.Add(new VoteOptionView(i, daVotare.Phrases[i]));
                }

                HasVoted = false;
                VotedCount = 0;
                VotersExpected = 0;
                Screen = ScreenState.Voting;
                break;

            case VoteProgressMessage avanzamento:
                VotedCount = avanzamento.Voted;
                VotersExpected = avanzamento.Total;
                break;

            case GameFinishedMessage finale:
                FinalResults.Clear();
                foreach (var risultato in finale.Results)
                {
                    FinalResults.Add(new PhraseResultRowView(risultato, IsHost));
                }

                Screen = ScreenState.Finished;
                break;

            case IllustrationReadyMessage pronta:
                // Nome diverso da "riga" (usato nel case RoomStateMessage qui
                // sopra, dentro il foreach): i case di uno switch senza
                // parentesi condividono lo stesso ambito, quindi lo stesso nome
                // qui sarebbe un conflitto di dichiarazione.
                if (RigaDiFrase(pronta.PhraseIndex) is { } rigaPronta)
                {
                    rigaPronta.IsWaiting = false;
                    // L'indirizzo arriva relativo perché il server non sa sotto
                    // quale nome è raggiunto: davanti c'è un reverse proxy.
                    // ServerUrl è l'unico posto dove quell'informazione c'è.
                    rigaPronta.ImageUrl = new Uri(new Uri(ServerUrl), pronta.Path).ToString();
                }

                break;

            case IllustrationFailedMessage fallita:
                // Il fallimento va in broadcast a tutta la stanza (il server
                // non sa chi ha premuto il pulsante), ma il banner deve
                // parlare solo a chi ha davvero chiesto l'illustrazione: gli
                // altri client non hanno niente da riprovare, e un errore per
                // qualcosa che non hanno chiesto non significa nulla sui loro
                // schermi. IsWaiting su QUESTO client è vero solo per chi ha
                // premuto il pulsante - RequestIllustrationAsync lo accende
                // prima dell'await - quindi va letto PRIMA di spegnerlo qui
                // sotto, che invece resta per tutti: è lo spegnimento
                // dell'attesa a dover valere ovunque, non il messaggio.
                //
                // ErrorText va scritto solo se la riga esiste ancora: un
                // fallimento tardivo (partita già ricominciata, FinalResults
                // svuotata da PulisciStatoDiPartitaConclusa) non ha più niente
                // a cui riferirsi, e mostrare comunque il banner produrrebbe
                // un errore orfano - a schermo ma senza alcun contesto che lo
                // spieghi, perché la classifica a cui apparteneva è sparita.
                if (RigaDiFrase(fallita.PhraseIndex) is { } rigaFallita)
                {
                    var questoClientAvevaChiesto = rigaFallita.IsWaiting;
                    rigaFallita.IsWaiting = false;

                    if (questoClientAvevaChiesto)
                    {
                        ErrorText = fallita.Message;
                    }
                }

                break;

            case ErrorMessage errore:
                ErrorText = errore.Message;
                break;

            // Nessun banner: dal punto di vista dell'utente non è un errore,
            // è solo "quella partita non c'è più" (design rientro §5.2).
            case RejoinRejectedMessage:
                _roomSession.Clear();
                RoomCode = string.Empty;
                Screen = ScreenState.Home;
                break;
        }
    }

    /// <summary>
    /// Torna null se l'indice non è in classifica. Non è paranoia: il messaggio
    /// arriva dal server e il client non ha modo di verificarlo, e una riga
    /// cercata per posizione in una lista ordinata per voti sarebbe comunque
    /// la riga sbagliata.
    /// </summary>
    private PhraseResultRowView? RigaDiFrase(int phraseIndex) =>
        FinalResults.FirstOrDefault(r => r.PhraseIndex == phraseIndex);

    /// <summary>
    /// Ripulisce lo stato accumulato dalla partita appena conclusa - risultati
    /// finali, caselle rivelate - prima di tornare alla lobby o ricominciarne
    /// subito un'altra (lotto-d-brief.md). Il server manda comunque stato
    /// nuovo, ma senza questa pulizia le collezioni locali resterebbero
    /// appese: lo stesso genere di dimenticanza che aveva lasciato il testo
    /// di un bot in modifica dopo una ricostruzione della lista giocatori.
    /// </summary>
    private void PulisciStatoDiPartitaConclusa()
    {
        FinalResults.Clear();
        RevealFragments.Clear();
        _fraseCompleta = false;
        VoteOptions.Clear();
        HasVoted = false;
        VotedCount = 0;
        VotersExpected = 0;
        ExpandedImageUrl = null;
    }

    private void AggiornaEtichettaRevealButton() =>
        RevealButtonLabel = _fraseCompleta ? "Prossima frase" : "Rivela la prossima parola";

    // CanAddBot e CanChangeSchema dipendono da questi campi: nessuno di loro
    // notifica da solo le proprietà calcolate, quindi lo fanno questi hook
    // generati dal toolkit per ogni [ObservableProperty] coinvolto.
    partial void OnIsHostChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAddBot));
        OnPropertyChanged(nameof(CanChangeSchema));
        OnPropertyChanged(nameof(ShowFinishedActions));
        OnPropertyChanged(nameof(ShowCloseVoting));

        // Le righe di FinalResults, a differenza di Players, non si
        // ricostruiscono a ogni RoomStateMessage: nascono una volta sola con
        // GameFinishedMessage, che non torna. Se l'host si disconnette e il
        // motore promuove un altro giocatore, questo è l'unico punto in cui
        // la promozione può ancora raggiungere righe già esistenti - senza,
        // il nuovo host vedrebbe "Nuova partita" e "Torna alla lobby" (che
        // dipendono da IsHost qui sopra) ma nessun pulsante "Illustra".
        foreach (var riga in FinalResults)
        {
            riga.IsHost = value;
        }
    }

    partial void OnScreenChanged(ScreenState value)
    {
        OnPropertyChanged(nameof(CanAddBot));
        OnPropertyChanged(nameof(CanChangeSchema));
        OnPropertyChanged(nameof(ShowFinishedActions));
        OnPropertyChanged(nameof(ShowCloseVoting));

        // L'overlay vive sopra tutte le schermate, non dentro quella finale:
        // chi non è host cambia schermata per un messaggio del server, non
        // passando da PulisciStatoDiPartitaConclusa (che copre solo i comandi
        // che solo l'host può eseguire) - senza questo, l'immagine resterebbe
        // appesa sopra la schermata successiva per tutti gli altri giocatori.
        if (value != ScreenState.Finished)
        {
            ExpandedImageUrl = null;
        }
    }

    partial void OnPlayerCountChanged(int value) => OnPropertyChanged(nameof(CanAddBot));

    // SchemaSummary dipende da entrambi: né SchemaNome né SlotCount
    // notificano da soli la proprietà calcolata.
    partial void OnSchemaNomeChanged(string value) => OnPropertyChanged(nameof(SchemaSummary));

    partial void OnSlotCountChanged(int value) => OnPropertyChanged(nameof(SchemaSummary));
}
