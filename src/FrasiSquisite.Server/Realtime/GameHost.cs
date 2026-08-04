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
    ILogger<GameHost> logger)
{
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
                rifinite = await runner.RifinisciAsync(richiesta.Frasi, richiesta.Template, CancellationToken.None);
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

    public static string PlayerGroup(Guid playerId) => $"player:{playerId}";
}
