using System.Collections.Concurrent;
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Server.Ai;
using FrasiSquisite.Server.Images;
using FrasiSquisite.Server.Rooms;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FrasiSquisite.Server.Realtime;

/// <summary>
/// Esegue gli effetti prodotti dal motore. È l'unico punto del server che
/// conosce sia il dominio sia SignalR: il motore resta ignaro della rete
/// (spec §3.2).
/// </summary>
public sealed class GameHost(
    IGameEngine engine,
    IRoomRegistry rooms,
    IHubContext<GameHub> hub,
    RefinementRunner runner,
    IllustrationRunner illustrazioni,
    ImageStore deposito,
    ILogger<GameHost> logger,
    IGracePeriodTimer? timer = null)
{
    // Parametro opzionale invece che obbligatorio: i circa dieci test
    // esistenti che costruiscono GameHost a mano (GameHostTests.cs) non
    // hanno nulla a che fare col periodo di grazia, e forzarli tutti a
    // passare un ottavo argomento sarebbe puro rumore. In produzione
    // Program.cs registra comunque il timer vero esplicitamente in DI.
    private readonly IGracePeriodTimer _timer = timer ?? new RealGracePeriodTimer();

    /// <summary>
    /// Un lucchetto per codice stanza. <b>Deve restare un campo d'istanza, non
    /// statico:</b> protegge lo stato tenuto da <see cref="IRoomRegistry"/>,
    /// che vive nel container di dipendenze, quindi deve avere lo stesso
    /// ambito. Da <c>static</c> la tabella sopravviveva al container e veniva
    /// condivisa fra host diversi nello stesso processo — in produzione senza
    /// conseguenze, perché <c>GameHost</c> è registrato come singleton e di
    /// host ce n'è uno solo, ma nei test due host indipendenti che pescano lo
    /// stesso codice stanza finivano per serializzarsi a vicenda pur non
    /// avendo nulla in comune.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Un periodo di grazia in corso per (stanza, giocatore): rientrare
    /// prima che scada annulla il dispatch di PlayerLeft sottostante, senza
    /// che nessun bot prenda mai il posto (design rientro §3.1). Campo
    /// d'istanza per lo stesso motivo di <see cref="_locks"/>.
    /// </summary>
    private readonly ConcurrentDictionary<(string RoomCode, Guid PlayerId), CancellationTokenSource> _periodiDiGrazia =
        new();

    /// <summary>
    /// La connessione SignalR attualmente valida per (stanza, giocatore):
    /// scritta a ogni ingresso riuscito (creazione, join, rientro). Serve ad
    /// AvviaPeriodoDiGrazia per riconoscere una disconnessione arrivata in
    /// ritardo da una connessione già superata da un rientro riuscito su
    /// una connessione più recente — altrimenti riarmerebbe un periodo di
    /// grazia per un giocatore che sta già giocando altrove (rilievo
    /// Critical 1 della revisione finale).
    /// </summary>
    private readonly ConcurrentDictionary<(string RoomCode, Guid PlayerId), string> _connessioniCorrenti = new();

    private static readonly TimeSpan DurataPeriodoDiGrazia = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Serializza gli eventi per stanza: due invii simultanei leggerebbero lo
    /// stesso stato e si sovrascriverebbero a vicenda.
    /// </summary>
    /// <exception cref="HubException">
    /// La stanza non esiste (es. persa per un riavvio del server): senza
    /// questo segnale il chiamante crederebbe che il comando sia andato a
    /// buon fine e la partita si bloccherebbe in silenzio.
    /// </exception>
    public async Task DispatchAsync(string roomCode, GameEvent evt, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            if (!rooms.TryGet(roomCode, out var stato))
            {
                throw new HubException("Stanza non trovata.");
            }

            var risultato = engine.Handle(stato, evt);
            rooms.Set(roomCode, risultato.State);

            foreach (var effetto in risultato.Effects)
            {
                try
                {
                    await EseguiAsync(roomCode, effetto, ct);
                }
                catch (Exception ex)
                {
                    // Non viene inghiottita: il chiamante deve comunque vedere il
                    // fallimento, ma senza questo log resterebbe invisibile sul
                    // percorso di disconnessione, dove non c'è alcun client a cui
                    // segnalarlo (spec §3.2).
                    logger.LogError(
                        ex,
                        "Invio effetto fallito per la stanza {RoomCode}, tipo effetto {EffectType}.",
                        roomCode,
                        effetto.GetType().Name);
                    throw;
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private Task EseguiAsync(string roomCode, Effect effetto, CancellationToken ct) => effetto switch
    {
        BroadcastToRoom b => hub.Clients.Group(roomCode)
            .SendAsync("ReceiveMessage", b.Message.GetType().Name, b.Message, ct),

        SendToPlayer s => hub.Clients.Group(PlayerGroup(s.PlayerId))
            .SendAsync("ReceiveMessage", s.Message.GetType().Name, s.Message, ct),

        // NON si attende, ed e' deliberato. DispatchAsync tiene il lucchetto
        // della stanza per tutta l'esecuzione degli effetti: aspettare qui
        // terrebbe fuori ogni altro evento per tutta la durata della chiamata
        // al modello, e - peggio - il risultato deve rientrare come EVENTO,
        // cioe' con un'altra DispatchAsync sulla stessa stanza, che
        // aspetterebbe quello stesso lucchetto. Stallo.
        RequestRefinement r => AvviaRifinitura(roomCode, r),

        // Stessa ragione di RequestRefinement: non si attende, o il ritorno
        // andrebbe in stallo sul lucchetto della stanza.
        RequestIllustration i => AvviaIllustrazione(roomCode, i),

        _ => throw new InvalidOperationException($"Effetto non gestito: {effetto.GetType().Name}"),
    };

    /// <summary>
    /// Avvia la rifinitura in sottofondo e ritorna subito. Il risultato
    /// rientra come evento, quando il lucchetto della stanza e' gia' stato
    /// rilasciato.
    /// </summary>
    private Task AvviaRifinitura(string roomCode, RequestRefinement richiesta)
    {
        _ = Task.Run(async () =>
        {
            IReadOnlyList<IReadOnlyList<string>>? rifinite = null;

            try
            {
                rifinite = await runner.RifinisciAsync(richiesta.Frasi, richiesta.Template, richiesta.Ruoli, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Il runner non dovrebbe lanciare, ma questo e' un task
                // slegato: un'eccezione non osservata qui lascerebbe la
                // stanza in Refining per sempre, e nessuno lo saprebbe.
                logger.LogError(ex, "Rifinitura fallita per la stanza {RoomCode}.", roomCode);
            }

            try
            {
                await DispatchAsync(roomCode, new RefinementFinished(rifinite));
            }
            catch (Exception ex)
            {
                // La stanza puo' essere sparita nel frattempo (riavvio, o
                // tutti usciti): non c'e' piu' nessuno a cui importi, ma
                // resta l'unica traccia osservabile.
                logger.LogWarning(ex, "Esito della rifinitura non consegnabile alla stanza {RoomCode}.", roomCode);
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Genera in sottofondo e torna subito, per la stessa ragione di
    /// <see cref="AvviaRifinitura"/>. I byte non escono mai da qui: finiscono
    /// nel deposito, e l'evento di ritorno porta solo il percorso — il motore
    /// non vede mai un PNG (spec §5).
    ///
    /// Al più un <see cref="IllustrationFinished"/> per ogni richiesta: questo
    /// metodo viene invocato una volta per ogni <see cref="RequestIllustration"/>
    /// (il motore rifiuta un secondo tocco sullo stesso indice con
    /// ILLUSTRATION_ALREADY_REQUESTED, vedi GameEngine.Illustration), e la
    /// dispatch dell'esito qui sotto avviene una volta sola per invocazione.
    /// La garanzia conta perché il motore, a differenza della rifinitura,
    /// lascia l'indice nell'insieme dopo un successo apposta per impedire un
    /// secondo pagamento: un esito di successo duplicato per lo stesso indice
    /// verrebbe ribroadcastato invece che ignorato.
    /// </summary>
    private Task AvviaIllustrazione(string roomCode, RequestIllustration richiesta)
    {
        _ = Task.Run(async () =>
        {
            string? percorso = null;

            try
            {
                var byteImmagine = await illustrazioni.IllustraAsync(richiesta.Frase, CancellationToken.None);

                if (byteImmagine is not null)
                {
                    // Salva torna null se l'immagine da sola supera l'intero
                    // budget del deposito: un salvataggio fallito è un
                    // fallimento della generazione a tutti gli effetti, non un
                    // successo con un percorso che punterebbe al nulla.
                    percorso = deposito.Salva(byteImmagine);
                }
            }
            catch (Exception ex)
            {
                // Task slegato: un'eccezione non osservata lascerebbe il
                // pulsante spento per sempre, e nessuno lo saprebbe.
                logger.LogError(ex, "Illustrazione fallita per la stanza {RoomCode}.", roomCode);
            }

            try
            {
                await DispatchAsync(roomCode, new IllustrationFinished(richiesta.PhraseIndex, percorso));
            }
            catch (Exception ex)
            {
                // La stanza puo' essere sparita nel frattempo (riavvio, o
                // tutti usciti): non c'e' piu' nessuno a cui importi, ma
                // resta l'unica traccia osservabile.
                logger.LogWarning(ex, "Esito dell'illustrazione non consegnabile alla stanza {RoomCode}.", roomCode);
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Avvia il periodo di grazia per una disconnessione: se non annullato
    /// da un rientro entro 30s, dispatcha PlayerLeft esattamente come faceva
    /// prima GameHub.OnDisconnectedAsync in modo sincrono (design rientro
    /// §3.1). Sganciato e senza attesa, stesso stile di AvviaRifinitura.
    /// </summary>
    public void AvviaPeriodoDiGrazia(string roomCode, Guid playerId, string connectionId)
    {
        if (!EConnessioneCorrente(roomCode, playerId, connectionId))
        {
            return;
        }

        var cts = new CancellationTokenSource();

        // Due disconnessioni di fila per lo stesso giocatore prima di un
        // rientro riuscito lascerebbero il primo periodo di grazia orfano
        // se non fosse annullato qui: senza questo passo continuerebbe a
        // contare per conto suo e, scadendo più tardi, dispatcherebbe
        // PlayerLeft anche dopo che il rientro (sul secondo periodo) è
        // già avvenuto — l'esatto scenario che questa funzionalità esiste
        // per evitare.
        //
        // Deliberatamente NON si chiama anche precedente.Dispose() qui: il
        // Task.Run del periodo precedente potrebbe non aver ancora letto
        // precedente.Token (l'ha ancora da valutare come argomento della
        // propria chiamata a DelayAsync piu' sotto). Un token letto da un
        // CancellationTokenSource gia' disposto lancia
        // ObjectDisposedException invece di restituire un token cancellato,
        // e quell'eccezione uscirebbe non osservata da un Task fire-and-forget
        // impedendo per sempre a quel periodo di raggiungere il proprio
        // ramo "annullato" qui sotto. Cancel() da solo e' sufficiente: un
        // token gia' cancellato, letto in sicurezza anche dopo, fa comunque
        // terminare quel Task.Run con OperationCanceledException.
        if (_periodiDiGrazia.TryRemove((roomCode, playerId), out var precedente))
        {
            precedente.Cancel();
        }

        _periodiDiGrazia[(roomCode, playerId)] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await _timer.DelayAsync(DurataPeriodoDiGrazia, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Annullato da un rientro in tempo, o soppiantato da una
                // seconda disconnessione (AnnullaPeriodoDiGrazia o la nuova
                // AvviaPeriodoDiGrazia hanno già rimosso e cancellato questo
                // stesso token): nessun PlayerLeft da dispatchare.
                return;
            }

            // Scaduto senza essere annullato. La rimozione è sicura
            // sull'identità: rimuove (e dispatcha PlayerLeft) solo se il
            // proprio token è ancora quello registrato per questa chiave.
            // Seconda linea di difesa oltre al passo sopra: se per qualunque
            // motivo questo periodo di grazia fosse comunque diventato
            // orfano (sostituito o già annullato), qui si accorge di non
            // essere più quello corrente e non dispatcha nulla — invece di
            // espellere un giocatore che nel frattempo è già rientrato.
            var rimosso = ((ICollection<KeyValuePair<(string, Guid), CancellationTokenSource>>)_periodiDiGrazia)
                .Remove(new KeyValuePair<(string, Guid), CancellationTokenSource>((roomCode, playerId), cts));

            if (!rimosso)
            {
                return;
            }

            try
            {
                await DispatchAsync(roomCode, new PlayerLeft(playerId));
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Disconnessione del giocatore {PlayerId} dalla stanza {RoomCode}: dispatch di PlayerLeft fallita dopo il periodo di grazia.",
                    playerId,
                    roomCode);
            }
        });
    }

    /// <summary>
    /// Disconnessione in lobby: nessun periodo di grazia, PlayerLeft
    /// dispatchato subito. In lobby N non è ancora fissato e un rientro
    /// tardivo non è comunque previsto (design rientro §6 — GameHub.RejoinRoom
    /// rifiuterebbe comunque un giocatore già rimosso dalla lista), quindi
    /// aspettare 30s farebbe solo apparire per quel tempo un giocatore che
    /// se n'è già andato (rilievo Important 5 della revisione finale).
    /// </summary>
    public async Task EvictImmediatelyAsync(string roomCode, Guid playerId, string connectionId)
    {
        if (!EConnessioneCorrente(roomCode, playerId, connectionId))
        {
            return;
        }

        try
        {
            await DispatchAsync(roomCode, new PlayerLeft(playerId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Disconnessione in lobby del giocatore {PlayerId} dalla stanza {RoomCode}: dispatch di PlayerLeft fallita.",
                playerId,
                roomCode);
        }
    }

    /// <summary>
    /// Annulla un periodo di grazia pendente: chiamato da
    /// GameHub.RejoinRoom prima di dispatchare PlayerRejoined. Nessun
    /// effetto se non ce n'era uno (rientro senza una disconnessione
    /// recente, o periodo già scaduto).
    /// </summary>
    public void AnnullaPeriodoDiGrazia(string roomCode, Guid playerId)
    {
        // Solo Cancel(), niente Dispose(): stesso motivo documentato in
        // AvviaPeriodoDiGrazia. Il Task.Run di questo periodo potrebbe non
        // aver ancora letto cts.Token; disposto qui, quella lettura
        // lancerebbe ObjectDisposedException invece di vedere un token
        // già cancellato, e l'eccezione — non osservata su un task
        // fire-and-forget — impedirebbe a quel periodo di raggiungere il
        // proprio ramo "annullato" e di terminare pulito.
        if (_periodiDiGrazia.TryRemove((roomCode, playerId), out var cts))
        {
            cts.Cancel();
        }
    }

    public void RegistraConnessione(string roomCode, Guid playerId, string connectionId) =>
        _connessioniCorrenti[(roomCode, playerId)] = connectionId;

    /// <summary>
    /// Vero se questa connessione è ancora quella corrente per (stanza,
    /// giocatore) — o se non ce n'è mai stata registrata nessuna (il
    /// giocatore non è mai stato registrato, es. nei test unitari che
    /// chiamano questi metodi direttamente). Condivisa da
    /// AvviaPeriodoDiGrazia ed EvictImmediatelyAsync: entrambi i percorsi di
    /// disconnessione devono ignorare una notifica arrivata in ritardo da
    /// una connessione già superata da un rientro riuscito altrove.
    /// </summary>
    private bool EConnessioneCorrente(string roomCode, Guid playerId, string connectionId) =>
        !_connessioniCorrenti.TryGetValue((roomCode, playerId), out var corrente) || corrente == connectionId;

    public static string PlayerGroup(Guid playerId) => $"player:{playerId}";
}
