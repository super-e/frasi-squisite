using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Validation;

namespace FrasiSquisite.Domain.Engine;

public sealed class GameEngine(IGameMode mode, IWordPool pool, IRandomSource random) : IGameEngine
{
    public const int MinPlayers = 2;

    /// <summary>
    /// Dal design: il pulsante "aggiungi bot" scompare a 9 giocatori
    /// (lotto-b-brief.md).
    /// </summary>
    public const int MaxPlayers = 9;

    /// <summary>
    /// Nomi assegnati ai bot, in ordine, deterministicamente: primo libero
    /// della lista, mai casuale (lotto-b-brief.md, punto 2). Otto voci perché
    /// con <see cref="MaxPlayers"/> a 9 e almeno un host umano, otto bot sono
    /// il massimo possibile in una stanza.
    /// </summary>
    private static readonly string[] BotNames =
        ["Bot Ada", "Bot Bruno", "Bot Chiara", "Bot Delia", "Bot Enzo", "Bot Fiamma", "Bot Gigi", "Bot Ivo"];

    private readonly IGameMode _mode = mode;
    private readonly IWordPool _pool = pool;
    private readonly IRandomSource _random = random;

    public EngineResult Handle(GameState state, GameEvent evt) => evt switch
    {
        PlayerJoined e => OnPlayerJoined(state, e),
        PlayerLeft e => OnPlayerLeft(state, e),
        GameStartRequested e => OnGameStartRequested(state, e),
        SlotSubmitted e => OnSlotSubmitted(state, e),
        RevealAdvanceRequested e => OnRevealAdvance(state, e),
        NewGameRequested e => OnNewGameRequested(state, e),
        BackToLobbyRequested e => OnBackToLobbyRequested(state, e),
        BotAdded e => OnBotAdded(state, e),
        BotRemoved e => OnBotRemoved(state, e),
        BotRenamed e => OnBotRenamed(state, e),
        SchemaSelected e => OnSchemaSelected(state, e),
        _ => EngineResult.NoChange(state),
    };

    private static EngineResult OnPlayerJoined(GameState state, PlayerJoined e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.PlayerId, "GAME_IN_PROGRESS", "La partita è già iniziata.");
        }

        if (state.FindPlayer(e.PlayerId) is not null)
        {
            return new EngineResult(state, [new BroadcastToRoom(RoomState(state))]);
        }

        var giocatore = new Player(e.PlayerId, e.Nickname, IsBot: false, state.NextJoinOrder, IsConnected: true);

        var nuovo = state with
        {
            Players = [.. state.Players, giocatore],
            NextJoinOrder = state.NextJoinOrder + 1,
            HostId = state.Players.Count == 0 ? e.PlayerId : state.HostId,
        };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    private EngineResult OnPlayerLeft(GameState state, PlayerLeft e)
    {
        var uscente = state.FindPlayer(e.PlayerId);
        if (uscente is null)
        {
            return EngineResult.NoChange(state);
        }

        // In lobby si esce davvero: N non è ancora fissato.
        if (state.Phase == RoomPhase.Lobby)
        {
            var rimasti = state.Players.Where(p => p.Id != e.PlayerId).ToList();

            // Filtrato su IsConnected come nel ramo Writing sotto: un bot non
            // è mai connesso, quindi non deve mai ereditare l'host solo
            // perché è il rimasto con JoinOrder più basso (lotto-b-brief.md:
            // "un bot non può mai diventare host").
            var host = state.HostId == e.PlayerId
                ? HostPiuAnzianoTraIConnessi(rimasti)
                : state.HostId;

            var senza = state with { Players = rimasti, HostId = host };

            return new EngineResult(senza, [new BroadcastToRoom(RoomState(senza))]);
        }

        // A partita iniziata il posto resta occupato: N è fissato (spec §2.2).
        // Il giocatore viene marcato disconnesso e da qui in poi gioca il bot.
        if (!uscente.IsConnected)
        {
            return EngineResult.NoChange(state);
        }

        var giocatori = state.Players
            .Select(p => p.Id == e.PlayerId ? p with { IsConnected = false } : p)
            .ToList();

        var nuovoHost = state.HostId == e.PlayerId
            ? giocatori.Where(p => p.IsConnected).OrderBy(p => p.JoinOrder).FirstOrDefault()?.Id ?? state.HostId
            : state.HostId;

        var aggiornato = state with { Players = giocatori, HostId = nuovoHost };

        List<Effect> effetti = [new BroadcastToRoom(RoomState(aggiornato))];

        if (aggiornato.Phase == RoomPhase.Writing)
        {
            return FillDisconnected(aggiornato, effetti);
        }

        return new EngineResult(aggiornato, effetti);
    }

    private EngineResult OnGameStartRequested(GameState state, GameStartRequested e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "GAME_IN_PROGRESS", "La partita è già iniziata.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può avviare.");
        }

        if (state.Players.Count < MinPlayers)
        {
            return Error(state, e.RequestedBy, "TOO_FEW_PLAYERS", $"Servono almeno {MinPlayers} giocatori.");
        }

        return StartGame(state);
    }

    private EngineResult StartGame(GameState state)
    {
        var frasi = Enumerable
            .Range(0, _mode.PhraseCount(state.Players.Count, state.Schema))
            .Select(i => Phrase.Empty(i, state.Schema.SlotCount))
            .ToList();

        var nuovo = state with
        {
            Phase = RoomPhase.Writing,
            Round = 0,
            Phrases = frasi,
            SubmittedThisRound = new HashSet<Guid>(),
        };

        List<Effect> effetti = [new BroadcastToRoom(RoomState(nuovo))];
        effetti.AddRange(SlotRequests(nuovo));

        // Punto 1 del brief: un bot presente al kick-off non è mai connesso,
        // e senza questo richiamo nessuno riempirebbe la sua casella del
        // round 0 - il round non si completerebbe mai e la partita
        // resterebbe bloccata al primo turno. AdvanceRound fa già lo stesso
        // controllo per i round successivi; StartGame deve farlo per il
        // round 0 esattamente allo stesso modo.
        if (nuovo.Players.Any(p => !p.IsConnected))
        {
            return FillDisconnected(nuovo, effetti);
        }

        return new EngineResult(nuovo, effetti);
    }

    private EngineResult OnSlotSubmitted(GameState state, SlotSubmitted e)
    {
        if (state.Phase != RoomPhase.Writing)
        {
            return Error(state, e.PlayerId, "NOT_WRITING", "Non è il momento di scrivere.");
        }

        var indice = state.IndexOfPlayer(e.PlayerId);
        if (indice < 0)
        {
            return Error(state, e.PlayerId, "NOT_IN_ROOM", "Non sei in questa stanza.");
        }

        if (state.SubmittedThisRound.Contains(e.PlayerId))
        {
            return Error(state, e.PlayerId, "ALREADY_SUBMITTED", "Hai già inviato per questo round.");
        }

        // Il server riapplica la validazione: non può fidarsi del client.
        var esito = SlotTextValidator.Validate(e.Text);
        if (!esito.IsValid)
        {
            return Error(state, e.PlayerId, "INVALID_TEXT", esito.Error!);
        }

        var nuovo = ApplySlot(state, e.PlayerId, esito.Normalized);

        if (nuovo.SubmittedThisRound.Count < nuovo.Players.Count)
        {
            return new EngineResult(nuovo, [
                new BroadcastToRoom(new RoundProgressMessage(
                    nuovo.Round, nuovo.SubmittedThisRound.Count, nuovo.Players.Count)),
            ]);
        }

        return AdvanceRound(nuovo);
    }

    private GameState ApplySlot(GameState state, Guid playerId, string testoNormalizzato)
    {
        var indice = state.IndexOfPlayer(playerId);
        var assegnazione = _mode.AssignSlot(state.Round, indice, state.Players.Count, state.Schema);

        var frasi = state.Phrases.ToArray();
        frasi[assegnazione.PhraseIndex] = frasi[assegnazione.PhraseIndex]
            .With(assegnazione.SlotIndex, new Slot(playerId, testoNormalizzato));

        return state with
        {
            Phrases = frasi,
            SubmittedThisRound = new HashSet<Guid>(state.SubmittedThisRound) { playerId },
        };
    }

    /// <summary>
    /// Riempie con il bot la casella di ogni giocatore disconnesso che non ha
    /// ancora inviato in questo round, e fa avanzare il round se con questo
    /// tutti hanno una casella. Nessuno resta mai in attesa di chi non c'è.
    /// </summary>
    private EngineResult FillDisconnected(GameState state, List<Effect> effetti)
    {
        var corrente = state;

        foreach (var giocatore in state.Players.Where(p => !p.IsConnected))
        {
            if (corrente.SubmittedThisRound.Contains(giocatore.Id))
            {
                continue;
            }

            corrente = ApplySlot(corrente, giocatore.Id, BotWord(corrente, giocatore.Id));
        }

        if (corrente.SubmittedThisRound.Count < corrente.Players.Count)
        {
            effetti.Add(new BroadcastToRoom(new RoundProgressMessage(
                corrente.Round, corrente.SubmittedThisRound.Count, corrente.Players.Count)));

            return new EngineResult(corrente, effetti);
        }

        var avanzato = AdvanceRound(corrente);

        return new EngineResult(avanzato.State, [.. effetti, .. avanzato.Effects]);
    }

    private string BotWord(GameState state, Guid playerId)
    {
        var indice = state.IndexOfPlayer(playerId);
        var assegnazione = _mode.AssignSlot(state.Round, indice, state.Players.Count, state.Schema);

        return _pool.Take(state.Schema.Caselle[assegnazione.SlotIndex].Ruolo, _random);
    }

    private EngineResult AdvanceRound(GameState state)
    {
        var prossimo = state with
        {
            Round = state.Round + 1,
            SubmittedThisRound = new HashSet<Guid>(),
        };

        if (_mode.IsComplete(prossimo))
        {
            var reveal = prossimo with
            {
                Phase = RoomPhase.Reveal,
                RevealPhraseIndex = 0,
                RevealSlotCount = 0,
            };

            // Nessuna SlotRequestMessage arriva più: senza altri messaggi ogni
            // client resterebbe fermo sulla schermata di attesa (Screen non
            // cambia mai da solo). Il RoundProgressMessage saturo evita che
            // l'attesa resti bloccata a N-1/N, e la RevealStepMessage iniziale
            // - vuota, nessuna casella ancora scoperta - è ciò che porta tutti
            // sulla schermata di reveal: solo l'host può poi far avanzare lo
            // scoprimento vero e proprio (RevealAdvanceRequested).
            return new EngineResult(reveal, [
                new BroadcastToRoom(RoomState(reveal)),
                new BroadcastToRoom(new RoundProgressMessage(state.Round, state.Players.Count, state.Players.Count)),
                new BroadcastToRoom(new RevealStepMessage(0, reveal.Phrases.Count, [], false, [])),
            ]);
        }

        List<Effect> effetti = [
            new BroadcastToRoom(new RoundProgressMessage(state.Round, state.Players.Count, state.Players.Count)),
        ];
        effetti.AddRange(SlotRequests(prossimo));

        if (prossimo.Players.Any(p => !p.IsConnected))
        {
            return FillDisconnected(prossimo, effetti);
        }

        return new EngineResult(prossimo, effetti);
    }

    private IEnumerable<Effect> SlotRequests(GameState state)
    {
        for (var i = 0; i < state.Players.Count; i++)
        {
            var assegnazione = _mode.AssignSlot(state.Round, i, state.Players.Count, state.Schema);
            var casella = state.Schema.Caselle[assegnazione.SlotIndex];

            // Nota: PhraseIndex resta deliberatamente fuori dal messaggio.
            yield return new SendToPlayer(
                state.Players[i].Id,
                new SlotRequestMessage(
                    state.Round,
                    state.Schema.SlotCount,
                    casella.Ruolo,
                    casella.Prompt,
                    casella.Esempio));
        }
    }

    private static EngineResult OnRevealAdvance(GameState state, RevealAdvanceRequested e)
    {
        if (state.Phase != RoomPhase.Reveal)
        {
            return Error(state, e.RequestedBy, "NOT_REVEALING", "Non è il momento del reveal.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza fa avanzare il reveal.");
        }

        var scoperte = state.RevealSlotCount + 1;
        var frase = state.Phrases[state.RevealPhraseIndex];
        var completa = scoperte >= frase.Slots.Count;

        var testi = frase.Slots.Take(scoperte).Select(s => s!.Text).ToList();

        // Gli autori compaiono solo a frase completa (spec §2.4).
        var autori = completa
            ? frase.Slots.Select(s => state.FindPlayer(s!.AuthorId)?.Nickname ?? "?").ToList()
            : [];

        var passo = new RevealStepMessage(
            state.RevealPhraseIndex,
            state.Phrases.Count,
            testi,
            completa,
            autori);

        if (!completa)
        {
            return new EngineResult(
                state with { RevealSlotCount = scoperte },
                [new BroadcastToRoom(passo)]);
        }

        var prossimaFrase = state.RevealPhraseIndex + 1;

        if (prossimaFrase < state.Phrases.Count)
        {
            return new EngineResult(
                state with { RevealPhraseIndex = prossimaFrase, RevealSlotCount = 0 },
                [new BroadcastToRoom(passo)]);
        }

        var finito = state with { Phase = RoomPhase.Finished };

        var frasiComposte = finito.Phrases
            .Select(f => finito.Schema.Compose([.. f.Slots.Select(s => s!.Text)]))
            .ToList();

        return new EngineResult(finito, [
            new BroadcastToRoom(passo),
            new BroadcastToRoom(new GameFinishedMessage(frasiComposte)),
        ]);
    }

    /// <summary>
    /// "Nuova partita" (lotto-d-brief.md): riparte subito dalla schermata
    /// finale, senza passare per una lobby visibile. L'azzeramento è quello
    /// condiviso con <see cref="OnBackToLobbyRequested"/>; l'avvio vero e
    /// proprio riusa <see cref="StartGame"/>, la stessa strada di
    /// <see cref="OnGameStartRequested"/> - compreso il riempimento dei non
    /// connessi al round 0 che fa funzionare i bot.
    /// </summary>
    private EngineResult OnNewGameRequested(GameState state, NewGameRequested e)
    {
        if (state.Phase != RoomPhase.Finished)
        {
            return Error(state, e.RequestedBy, "NOT_FINISHED", "Si può ricominciare solo dalla schermata finale.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può ricominciare.");
        }

        var azzerato = AzzeraPerNuovaPartita(state);

        if (azzerato.Players.Count < MinPlayers)
        {
            // Il vicolo cieco non si sposta semplicemente da Finished a un
            // altro vicolo cieco: restare in Lobby è ciò che permette
            // all'host di rimediare aggiungendo un bot (brief del lotto). Il
            // broadcast tiene comunque tutti al passo con l'espulsione dei
            // disconnessi appena avvenuta.
            return new EngineResult(azzerato, [
                new BroadcastToRoom(RoomState(azzerato)),
                new SendToPlayer(e.RequestedBy, new ErrorMessage("TOO_FEW_PLAYERS", $"Servono almeno {MinPlayers} giocatori.")),
            ]);
        }

        return StartGame(azzerato);
    }

    /// <summary>
    /// "Torna alla lobby" (lotto-d-brief.md): stesso azzeramento di
    /// <see cref="OnNewGameRequested"/>, ma qui ci si ferma - nessun avvio.
    /// </summary>
    private static EngineResult OnBackToLobbyRequested(GameState state, BackToLobbyRequested e)
    {
        if (state.Phase != RoomPhase.Finished)
        {
            return Error(state, e.RequestedBy, "NOT_FINISHED", "Si può tornare alla lobby solo dalla schermata finale.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può tornare alla lobby.");
        }

        var azzerato = AzzeraPerNuovaPartita(state);

        return new EngineResult(azzerato, [new BroadcastToRoom(RoomState(azzerato))]);
    }

    /// <summary>
    /// Azzeramento condiviso da <see cref="OnNewGameRequested"/> e
    /// <see cref="OnBackToLobbyRequested"/> (lotto-d-brief.md): gli umani
    /// disconnessi escono dalla stanza (hanno lasciato, non devono restare a
    /// farsi giocare da un bot all'infinito) mentre i bot e gli umani
    /// connessi restano; round, frasi, invii e indici di reveal tornano allo
    /// stato iniziale; RoomCode, Schema, AvailableSchemas e NextJoinOrder non
    /// cambiano. Se l'host uscente era fra i tolti il ruolo passa al
    /// connesso presente da più tempo - la stessa regola già in
    /// <see cref="OnPlayerLeft"/>, non una terza variante.
    /// </summary>
    private static GameState AzzeraPerNuovaPartita(GameState state)
    {
        var rimasti = state.Players.Where(p => p.IsConnected || p.IsBot).ToList();

        var host = rimasti.Any(p => p.Id == state.HostId)
            ? state.HostId
            : HostPiuAnzianoTraIConnessi(rimasti);

        return state with
        {
            Phase = RoomPhase.Lobby,
            HostId = host,
            Players = rimasti,
            Round = 0,
            Phrases = [],
            SubmittedThisRound = new HashSet<Guid>(),
            RevealPhraseIndex = 0,
            RevealSlotCount = 0,
        };
    }

    /// <summary>
    /// Il connesso presente da più tempo tra i giocatori passati, o
    /// Guid.Empty se non ne resta nessuno: stessa regola di successione
    /// dell'host usata sia quando l'host abbandona la stanza
    /// (<see cref="OnPlayerLeft"/>) sia quando l'espulsione dei disconnessi
    /// al reset lo porta via (<see cref="AzzeraPerNuovaPartita"/>).
    /// </summary>
    private static Guid HostPiuAnzianoTraIConnessi(IEnumerable<Player> giocatori) =>
        giocatori.Where(p => p.IsConnected).OrderBy(p => p.JoinOrder).FirstOrDefault()?.Id ?? Guid.Empty;

    private static EngineResult OnBotAdded(GameState state, BotAdded e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "NOT_LOBBY", "I bot si aggiungono solo in lobby.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può aggiungere un bot.");
        }

        if (state.Players.Count >= MaxPlayers)
        {
            return Error(state, e.RequestedBy, "ROOM_FULL", $"La stanza ospita al massimo {MaxPlayers} giocatori.");
        }

        var nome = NextBotName(state);
        if (nome is null)
        {
            // Non è "irraggiungibile": NextBotName confronta con i nickname di
            // TUTTI i giocatori, umani inclusi. Con meno di MaxPlayers persone
            // già in stanza (quindi ROOM_FULL non è scattato) ma otto di loro
            // con un nickname che combacia esattamente con la lista dei nomi
            // bot, la lista si esaurisce comunque. Improbabile, ma un errore
            // pulito verso il client è comunque dovuto invece di un'eccezione
            // non gestita che risalirebbe fuori da Handle.
            return Error(state, e.RequestedBy, "NO_BOT_NAMES_LEFT",
                "Nessun nome disponibile per il bot: troppi giocatori hanno già un nome della lista.");
        }

        var bot = new Player(e.BotId, nome, IsBot: true, state.NextJoinOrder, IsConnected: false);

        var nuovo = state with
        {
            Players = [.. state.Players, bot],
            NextJoinOrder = state.NextJoinOrder + 1,
        };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    private static EngineResult OnBotRemoved(GameState state, BotRemoved e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "NOT_LOBBY", "I bot si rimuovono solo in lobby.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può rimuovere un bot.");
        }

        var bersaglio = state.FindPlayer(e.BotId);
        if (bersaglio is null)
        {
            return Error(state, e.RequestedBy, "NO_SUCH_PLAYER", "Questo giocatore non esiste più.");
        }

        if (!bersaglio.IsBot)
        {
            return Error(state, e.RequestedBy, "NOT_A_BOT", "Solo i bot si possono rimuovere così.");
        }

        var nuovo = state with { Players = [.. state.Players.Where(p => p.Id != e.BotId)] };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    private static EngineResult OnBotRenamed(GameState state, BotRenamed e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "NOT_LOBBY", "I bot si rinominano solo in lobby.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può rinominare un bot.");
        }

        var bersaglio = state.FindPlayer(e.BotId);
        if (bersaglio is null)
        {
            return Error(state, e.RequestedBy, "NO_SUCH_PLAYER", "Questo giocatore non esiste più.");
        }

        if (!bersaglio.IsBot)
        {
            return Error(state, e.RequestedBy, "NOT_A_BOT", "Solo i bot si possono rinominare così.");
        }

        // Stesso validatore del client: server e client non possono divergere.
        var esito = NicknameValidator.Validate(e.Nickname);
        if (!esito.IsValid)
        {
            return Error(state, e.RequestedBy, "INVALID_NICKNAME", esito.Error!);
        }

        // Innocuo per il motore (gli id restano la chiave), ma vanificherebbe
        // la lista di nomi senza collisioni e confonderebbe la lista in
        // lobby: due righe con lo stesso nome sembrerebbero un bug.
        if (state.Players.Any(p => p.Id != e.BotId && p.Nickname == esito.Normalized))
        {
            return Error(state, e.RequestedBy, "INVALID_NICKNAME", "Questo nome è già usato da un altro giocatore.");
        }

        var giocatori = state.Players
            .Select(p => p.Id == e.BotId ? p with { Nickname = esito.Normalized } : p)
            .ToList();

        var nuovo = state with { Players = giocatori };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    /// <summary>
    /// Sostituisce <see cref="GameState.Schema"/> con quello già risolto
    /// dall'hub. Solo in lobby (nessuna partita in corso, quindi niente frasi
    /// o round da azzerare) e solo per l'host: stesse guardie di
    /// <see cref="OnBotAdded"/>.
    /// </summary>
    private static EngineResult OnSchemaSelected(GameState state, SchemaSelected e)
    {
        if (state.Phase != RoomPhase.Lobby)
        {
            return Error(state, e.RequestedBy, "NOT_LOBBY", "Lo schema si cambia solo in lobby.");
        }

        if (state.HostId != e.RequestedBy)
        {
            return Error(state, e.RequestedBy, "NOT_HOST", "Solo chi ha creato la stanza può cambiare lo schema.");
        }

        var nuovo = state with { Schema = e.Schema };

        return new EngineResult(nuovo, [new BroadcastToRoom(RoomState(nuovo))]);
    }

    /// <summary>
    /// Primo nome libero della lista fissa: nessuna casualità, nessuna
    /// collisione. Null se sono tutti già in uso (vedi il chiamante).
    /// </summary>
    private static string? NextBotName(GameState state) =>
        BotNames.FirstOrDefault(nome => state.Players.All(p => p.Nickname != nome));

    private static EngineResult Error(GameState state, Guid playerId, string code, string message) =>
        new(state, [new SendToPlayer(playerId, new ErrorMessage(code, message))]);

    private static RoomStateMessage RoomState(GameState state) =>
        new(
            state.RoomCode,
            state.Phase.ToString(),
            [.. state.Players.Select(p => new PlayerView(p.Id, p.Nickname, p.Id == state.HostId, p.IsConnected, p.IsBot))],
            state.Schema.Id,
            state.Schema.SlotCount,
            state.AvailableSchemas);
}
