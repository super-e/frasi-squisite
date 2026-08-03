using System.Collections.Concurrent;
using System.Reflection;
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Server.Ai;
using FrasiSquisite.Server.Realtime;
using FrasiSquisite.Server.Tests.Ai;
using FrasiSquisite.Shared.Schemas;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;


namespace FrasiSquisite.Server.Tests.Realtime;

public class GameHostTests
{
    /// <summary>
    /// La tabella dei lucchetti protegge lo stato tenuto da IRoomRegistry, che
    /// vive nel container di dipendenze: deve avere lo stesso ambito, quindi
    /// essere un campo d'istanza.
    ///
    /// Da <c>static</c> sopravviveva al container ed era condivisa fra host
    /// diversi nello stesso processo. In produzione non si notava, perché
    /// GameHost è un singleton e di host ce n'è uno solo; nei test invece due
    /// host indipendenti che pescavano lo stesso codice stanza si
    /// serializzavano a vicenda pur non avendo alcuno stato in comune.
    ///
    /// Il test ispeziona il tipo invece di misurare i tempi di due dispatch
    /// concorrenti: la proprietà da difendere è esattamente "questo campo non
    /// è statico", e un test cronometrico sarebbe a sua volta intermittente —
    /// cioè proprio il difetto che questa correzione elimina.
    /// </summary>
    [Fact]
    public void LaTabellaDeiLucchettiEPerIstanzaENonStatica()
    {
        var campi = typeof(GameHost)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ConcurrentDictionary<string, SemaphoreSlim>))
            .ToList();

        var campo = Assert.Single(campi);

        Assert.False(
            campo.IsStatic,
            $"'{campo.Name}' è statico: la tabella dei lucchetti verrebbe condivisa fra host " +
            "diversi nello stesso processo, mentre le stanze che protegge vivono nel container.");
    }

    /// <summary>
    /// Due host distinti non devono condividere nulla. Se il campo tornasse
    /// statico questa uguaglianza diventerebbe vera, quindi l'asserzione
    /// inversa fallirebbe.
    /// </summary>
    [Fact]
    public void DueHostHannoTabelleDeiLucchettiDistinte()
    {
        var campo = typeof(GameHost)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(ConcurrentDictionary<string, SemaphoreSlim>));

        // Le dipendenze restano nulle di proposito: il costruttore primario non
        // le valida e il test non chiama nulla che le usi, gli serve solo che
        // gli inizializzatori di campo girino.
        var uno = new GameHost(null!, null!, null!, null!, null!);
        var due = new GameHost(null!, null!, null!, null!, null!);

        Assert.NotNull(campo.GetValue(uno));
        Assert.NotNull(campo.GetValue(due));
        Assert.NotSame(campo.GetValue(uno), campo.GetValue(due));
    }

    // Le tre garanzie qui sotto proteggono AvviaRifinitura (spec del task
    // "AI Task 5"): il lucchetto si rilascia prima che la rifinitura finisca,
    // l'evento di esito parte anche se la chiamata al modello lancia, e una
    // stanza sparita nel frattempo non fa esplodere il compito slegato. Il
    // motore qui e' finto (FakeGameEngine): non serve giocare una partita
    // vera per provare l'adapter, e un motore vero renderebbe più lento
    // isolare il difetto quando uno di questi test va in rosso.

    private static GameState StanzaVuota(string codice) =>
        GameState.NewRoom(codice, new Schema("test", 1, "Test", [], "{0} {1}"));

    private static RefinementRunner CreaRunner(FakeAiTextProvider ai) =>
        new(ai, Options.Create(new AiOptions()), NullLogger<RefinementRunner>.Instance);

    /// <summary>
    /// Attende una condizione invece di dormire un tempo fisso: la suite ha
    /// già un fallimento intermittente non diagnosticato, e un altro sleep
    /// alla cieca lo peggiorerebbe soltanto. Il timeout resta un limite
    /// superiore, non l'attesa normale.
    /// </summary>
    private static async Task AttendiCondizioneAsync(Func<bool> condizione, TimeSpan timeout)
    {
        var scadenza = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < scadenza)
        {
            if (condizione())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condizione(), $"Condizione non soddisfatta entro {timeout}.");
    }

    /// <summary>
    /// Cattura i log senza passare da un provider e da un intero host web:
    /// qui serve solo intercettare cosa arriva a <c>ILogger&lt;GameHost&gt;</c>.
    /// </summary>
    private sealed class LoggerDiTest : ILogger<GameHost>
    {
        public ConcurrentQueue<(LogLevel Livello, Exception? Eccezione)> Voci { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Voci.Enqueue((logLevel, exception));
    }

    /// <summary>
    /// E' la proprietà per cui l'intero lavoro di AI Task 5 esiste, ed era
    /// protetta solo dalla lettura del codice. Se AvviaRifinitura restituisse
    /// per errore il Task del compito slegato invece di uno già completato,
    /// DispatchAsync aspetterebbe - dentro EseguiAsync - lo stesso lucchetto
    /// che il compito slegato deve poter riacquisire quando rientra come
    /// RefinementFinished: uno stallo, non un semplice rallentamento. Senza
    /// il Task.WhenAny qui sotto il sintomo nei test d'integrazione sarebbe
    /// uno stallo silenzioso fino al timeout dell'intera suite - non
    /// un'asserzione rossa leggibile - perché quel lucchetto non si libera
    /// mai da solo.
    /// </summary>
    [Fact]
    public async Task DispatchAsyncTornaSubitoMentreLaRifinituraEAncoraInVolo()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""",
            Ritardo = TimeSpan.FromMilliseconds(400),
        };

        var engine = new FakeGameEngine(evt => evt switch
        {
            GameStartRequested => [new RequestRefinement([["a", "b"]], "{0} {1}")],
            _ => [],
        });

        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));

        var host = new GameHost(engine, rooms, null!, CreaRunner(ai), NullLogger<GameHost>.Instance);

        var dispatch = host.DispatchAsync("STANZA", new GameStartRequested(Guid.NewGuid()));

        // Soglia ben sotto ai 400ms di ritardo dell'IA: se il lucchetto non
        // si liberasse prima che la rifinitura finisca, "dispatch" non
        // vincerebbe mai questa corsa.
        var vincitore = await Task.WhenAny(dispatch, Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.Same(dispatch, vincitore);
        await dispatch;

        // La rifinitura deve essere ancora in corso a questo punto: il
        // motore non ha ancora visto l'evento di esito.
        Assert.DoesNotContain(engine.EventiRicevuti, e => e is RefinementFinished);

        // E deve comunque arrivare, non restare dimenticata in sottofondo.
        await AttendiCondizioneAsync(
            () => engine.EventiRicevuti.Any(e => e is RefinementFinished),
            TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Senza il primo catch di AvviaRifinitura, un'eccezione del provider
    /// risalirebbe non osservata dal compito slegato e RefinementFinished non
    /// partirebbe mai: la stanza resterebbe ferma in Refining per sempre,
    /// senza che nessuno se ne accorga (nessun client è in ascolto su quel
    /// percorso).
    /// </summary>
    [Fact]
    public async Task SeLaChiamataAlModelloLanciaLEventoDiEsitoArrivaComunque()
    {
        var ai = new FakeAiTextProvider
        {
            ProssimoErrore = new InvalidOperationException("Guasto simulato del modello."),
        };

        var engine = new FakeGameEngine(evt => evt switch
        {
            GameStartRequested => [new RequestRefinement([["a", "b"]], "{0} {1}")],
            _ => [],
        });

        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));

        var host = new GameHost(engine, rooms, null!, CreaRunner(ai), NullLogger<GameHost>.Instance);

        await host.DispatchAsync("STANZA", new GameStartRequested(Guid.NewGuid()));

        await AttendiCondizioneAsync(
            () => engine.EventiRicevuti.Any(e => e is RefinementFinished),
            TimeSpan.FromSeconds(2));

        var esito = (RefinementFinished)engine.EventiRicevuti.Last(e => e is RefinementFinished);
        Assert.Null(esito.Frasi);
    }

    /// <summary>
    /// Senza il secondo catch di AvviaRifinitura, l'HubException di una
    /// stanza sparita risalirebbe non osservata dal compito slegato: nessun
    /// client la vedrebbe comunque (non c'è nessuno in ascolto su una stanza
    /// che non esiste più), quindi il log è l'unica traccia osservabile che
    /// resta - ed è quello che questo test verifica.
    /// </summary>
    [Fact]
    public async Task SeLaStanzaSparisceMentreLaChiamataEInVoloVieneSoloLoggato()
    {
        var ai = new FakeAiTextProvider
        {
            Risposta = """{"frasi": [{"caselle": ["a", "b"]}]}""",
            Ritardo = TimeSpan.FromMilliseconds(200),
        };

        var engine = new FakeGameEngine(evt => evt switch
        {
            GameStartRequested => [new RequestRefinement([["a", "b"]], "{0} {1}")],
            _ => [],
        });

        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));

        var logger = new LoggerDiTest();
        var host = new GameHost(engine, rooms, null!, CreaRunner(ai), logger);

        await host.DispatchAsync("STANZA", new GameStartRequested(Guid.NewGuid()));

        // La stanza sparisce mentre la chiamata al modello è ancora in volo
        // (riavvio del server, tutti usciti: spec §7.1). Quando il compito
        // slegato rientra con DispatchAsync(RefinementFinished), rooms.TryGet
        // fallisce.
        rooms.Remove("STANZA");

        await AttendiCondizioneAsync(
            () => logger.Voci.Any(v => v.Livello == LogLevel.Warning && v.Eccezione is HubException),
            TimeSpan.FromSeconds(2));

        // Il motore non deve mai vedere l'evento: la stanza non c'era più
        // quando è rientrato.
        Assert.DoesNotContain(engine.EventiRicevuti, e => e is RefinementFinished);
    }
}
