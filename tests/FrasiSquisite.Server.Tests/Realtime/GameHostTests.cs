using System.Collections.Concurrent;
using System.Reflection;
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Server.Ai;
using FrasiSquisite.Server.Images;
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
        var uno = new GameHost(null!, null!, null!, null!, null!, null!, null!);
        var due = new GameHost(null!, null!, null!, null!, null!, null!, null!);

        Assert.NotNull(campo.GetValue(uno));
        Assert.NotNull(campo.GetValue(due));
        Assert.NotSame(campo.GetValue(uno), campo.GetValue(due));
    }

    /// <summary>
    /// Non completa mai da solo: il test decide quando "scade" chiamando
    /// FaiScadere, o annullando il CancellationToken passato a DelayAsync
    /// (come farebbe AnnullaPeriodoDiGrazia con un token vero). Zero
    /// Task.Delay reali, come richiesto dal design del rientro (§7).
    ///
    /// Tiene traccia di ogni chiamata a DelayAsync separatamente (non una
    /// sola TaskCompletionSource condivisa): serve per il test sui periodi
    /// di grazia sovrapposti, dove lo stesso GameHost chiama DelayAsync due
    /// volte per lo stesso giocatore e i due periodi vanno fatti scadere in
    /// modo indipendente.
    ///
    /// AvviaPeriodoDiGrazia schedula DelayAsync su Task.Run, quindi il
    /// thread del test può arrivare a chiamare FaiScadere prima che la
    /// chiamata a DelayAsync corrispondente sia stata registrata: FaiScadere
    /// attende (con un semaforo, non un polling a occhio) finché la
    /// chiamata numero `indice` non compare, invece di indicizzare a vuoto
    /// una lista ancora incompleta.
    /// </summary>
    private sealed class TimerControllabile : IGracePeriodTimer
    {
        private readonly List<TaskCompletionSource> _chiamate = [];
        private readonly SemaphoreSlim _nuovaChiamata = new(0);

        public Task DelayAsync(TimeSpan durata, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource();
            ct.Register(() => tcs.TrySetCanceled(ct));

            lock (_chiamate)
            {
                _chiamate.Add(tcs);
            }

            _nuovaChiamata.Release();

            return tcs.Task;
        }

        /// <summary>
        /// Fa scadere la chiamata a DelayAsync numero <paramref name="indice"/>
        /// (0-based, nell'ordine di arrivo), attendendo che sia stata
        /// registrata se necessario, fino a <paramref name="timeout"/> (5s se
        /// omesso, come richiesto da ogni test esistente che aspetta una
        /// chiamata vera). Un timeout più breve serve solo al test che prova
        /// il contrario — che NESSUNA chiamata sia mai arrivata (rilievo
        /// Critical 1 della revisione finale, guardia di identità della
        /// connessione in AvviaPeriodoDiGrazia): lì aspettare 5s veri per
        /// ogni corsa della suite sarebbe solo spreco, la risposta è già
        /// nota dopo una manciata di millisecondi.
        /// </summary>
        public void FaiScadere(int indice = 0, TimeSpan? timeout = null)
        {
            var limite = timeout ?? TimeSpan.FromSeconds(5);

            while (true)
            {
                lock (_chiamate)
                {
                    if (indice < _chiamate.Count)
                    {
                        _chiamate[indice].TrySetResult();
                        return;
                    }
                }

                if (!_nuovaChiamata.Wait(limite))
                {
                    throw new TimeoutException(
                        $"Nessuna chiamata a DelayAsync con indice {indice} registrata entro il timeout.");
                }
            }
        }
    }

    /// <summary>
    /// La tabella dei periodi di grazia protegge lo stesso genere di stato
    /// per-stanza di _locks: stesso motivo, campo d'istanza mai statico.
    /// </summary>
    [Fact]
    public void LaTabellaDeiPeriodiDiGraziaEPerIstanzaENonStatica()
    {
        var campi = typeof(GameHost)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ConcurrentDictionary<(string, Guid), CancellationTokenSource>))
            .ToList();

        var campo = Assert.Single(campi);

        Assert.False(campo.IsStatic, $"'{campo.Name}' è statico: i periodi di grazia verrebbero condivisi fra host diversi.");
    }

    [Fact]
    public async Task IlPeriodoDiGraziaScadutoDispatchaPlayerLeft()
    {
        var engine = new FakeGameEngine(_ => []);
        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));
        var timer = new TimerControllabile();
        var giocatore = Guid.NewGuid();

        var host = new GameHost(engine, rooms, null!, null!, null!, null!, NullLogger<GameHost>.Instance, timer);

        host.AvviaPeriodoDiGrazia("STANZA", giocatore, "conn-1");
        timer.FaiScadere();

        await AttendiCondizioneAsync(
            () => engine.EventiRicevuti.Any(e => e is PlayerLeft pl && pl.PlayerId == giocatore),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task IlPeriodoDiGraziaAnnullatoNonDispatchaNulla()
    {
        var engine = new FakeGameEngine(_ => []);
        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));
        var timer = new TimerControllabile();
        var giocatore = Guid.NewGuid();

        var host = new GameHost(engine, rooms, null!, null!, null!, null!, NullLogger<GameHost>.Instance, timer);

        host.AvviaPeriodoDiGrazia("STANZA", giocatore, "conn-1");
        host.AnnullaPeriodoDiGrazia("STANZA", giocatore);

        // Margine reale, breve: dà tempo a un eventuale (erroneo) dispatch
        // di completare prima dell'asserzione negativa.
        await Task.Delay(100);
        Assert.DoesNotContain(engine.EventiRicevuti, e => e is PlayerLeft);
    }

    [Fact]
    public async Task DueDisconnessioniDiFilaPoiUnRientroNonDispatchanoMaiPlayerLeft()
    {
        var engine = new FakeGameEngine(_ => []);
        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));
        var timer = new TimerControllabile();
        var giocatore = Guid.NewGuid();

        var host = new GameHost(engine, rooms, null!, null!, null!, null!, NullLogger<GameHost>.Instance, timer);

        // Due disconnessioni di fila per lo stesso giocatore, prima che un
        // rientro ne annulli uno: senza il fix di AvviaPeriodoDiGrazia, il
        // primo periodo di grazia resterebbe orfano e dispatcherebbe
        // comunque PlayerLeft alla propria scadenza, anche dopo che il
        // rientro sotto è già riuscito.
        host.AvviaPeriodoDiGrazia("STANZA", giocatore, "conn-1");
        host.AvviaPeriodoDiGrazia("STANZA", giocatore, "conn-1");
        host.AnnullaPeriodoDiGrazia("STANZA", giocatore);

        // Prova a far scadere entrambi i DelayAsync sottostanti: nessuno dei
        // due deve produrre un dispatch, comunque vada a finire la corsa fra
        // le cancellazioni interne e queste chiamate.
        timer.FaiScadere(0);
        timer.FaiScadere(1);

        await Task.Delay(100);
        Assert.DoesNotContain(engine.EventiRicevuti, e => e is PlayerLeft);
    }

    /// <summary>
    /// Non chiama timer.FaiScadere(0) col timeout di 5s di default: con la
    /// guardia della connessione attiva (fix del rilievo Critical 1),
    /// AvviaPeriodoDiGrazia ritorna PRIMA di arrivare a chiamare
    /// DelayAsync — non c'è quindi alcuna chiamata numero 0 da far
    /// scadere, e aspettarla coi 5s di default bloccherebbe questo test
    /// (e quindi finirebbe comunque per fallire con un TimeoutException,
    /// non con l'asserzione qui sotto). Un timeout breve rende invece la
    /// mancata chiamata la controprova diretta che la guardia ha
    /// funzionato: senza la guardia (codice pre-fix) la chiamata arriva
    /// comunque, quasi subito, e Assert.Throws qui sotto fallirebbe
    /// perché FaiScadere non lancerebbe nulla — la prova RED di questo
    /// test.
    /// </summary>
    [Fact]
    public async Task UnaDisconnessioneArrivataInRitardoDaUnaConnessioneSuperataNonDispatchaNulla()
    {
        var engine = new FakeGameEngine(_ => []);
        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));
        var timer = new TimerControllabile();
        var giocatore = Guid.NewGuid();

        var host = new GameHost(engine, rooms, null!, null!, null!, null!, NullLogger<GameHost>.Instance, timer);

        // Il giocatore è già registrato su una connessione più recente
        // ("conn-nuova"), come farebbe GameHub.EntraAsync dopo un rientro
        // riuscito. La disconnessione che arriva sotto è di una connessione
        // vecchia ("conn-vecchia"), già superata: non deve riarmare nulla.
        host.RegistraConnessione("STANZA", giocatore, "conn-nuova");

        host.AvviaPeriodoDiGrazia("STANZA", giocatore, "conn-vecchia");

        Assert.Throws<TimeoutException>(() => timer.FaiScadere(0, TimeSpan.FromMilliseconds(300)));

        await Task.Delay(100);
        Assert.DoesNotContain(engine.EventiRicevuti, e => e is PlayerLeft);
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

    private static IllustrationRunner CreaIllustrationRunner(FakeAiTextProvider testo, FakeAiImageProvider immagine) =>
        new(testo, immagine, Options.Create(new AiOptions()), NullLogger<IllustrationRunner>.Instance);

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

        var host = new GameHost(engine, rooms, null!, CreaRunner(ai), null!, null!, NullLogger<GameHost>.Instance);

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

        var host = new GameHost(engine, rooms, null!, CreaRunner(ai), null!, null!, NullLogger<GameHost>.Instance);

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
        var host = new GameHost(engine, rooms, null!, CreaRunner(ai), null!, null!, logger);

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

    // Le quattro garanzie qui sotto proteggono AvviaIllustrazione (AI Task 5):
    // stessa forma delle tre di AvviaRifinitura sopra, perché lo stallo che
    // evitano è lo stesso - qui cambia solo l'effetto (RequestIllustration),
    // il runner (IllustrationRunner, che chiama testo e immagine) e l'evento
    // di ritorno (IllustrationFinished). La quarta è nuova: dimostra che i
    // byte generati finiscono davvero nel deposito e che l'evento porta solo
    // il percorso, mai i byte stessi (spec §5).

    /// <summary>
    /// Se l'host attendesse la generazione, terrebbe il lucchetto della
    /// stanza per tutta la durata della chiamata — e l'evento di ritorno, che
    /// passa da quello stesso lucchetto, non entrerebbe mai. Stallo. Il test
    /// lo dimostra con un provider d'immagine lento: la dispatch deve tornare
    /// subito.
    /// </summary>
    [Fact]
    public async Task LaDispatchNonAspettaLaGenerazione()
    {
        var testo = new FakeAiTextProvider("a penguin in a pinstripe suit");
        var immagine = new FakeAiImageProvider
        {
            Risposta = [1, 2, 3],
            Ritardo = TimeSpan.FromMilliseconds(400),
        };

        var engine = new FakeGameEngine(evt => evt switch
        {
            GameStartRequested => [new RequestIllustration(0, "un pinguino in doppiopetto")],
            _ => [],
        });

        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));

        var host = new GameHost(
            engine, rooms, null!, null!, CreaIllustrationRunner(testo, immagine), new ImageStore(), NullLogger<GameHost>.Instance);

        var dispatch = host.DispatchAsync("STANZA", new GameStartRequested(Guid.NewGuid()));

        // Soglia ben sotto ai 400ms di ritardo del provider: se il lucchetto
        // non si liberasse prima che la generazione finisca, "dispatch" non
        // vincerebbe mai questa corsa.
        var vincitore = await Task.WhenAny(dispatch, Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.Same(dispatch, vincitore);
        await dispatch;

        // La generazione deve essere ancora in corso a questo punto: il
        // motore non ha ancora visto l'evento di esito.
        Assert.DoesNotContain(engine.EventiRicevuti, e => e is IllustrationFinished);

        // E deve comunque arrivare, non restare dimenticata in sottofondo.
        await AttendiCondizioneAsync(
            () => engine.EventiRicevuti.Any(e => e is IllustrationFinished),
            TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Senza il primo catch di AvviaIllustrazione, un'eccezione del provider
    /// risalirebbe non osservata dal compito slegato e IllustrationFinished
    /// non partirebbe mai: il pulsante resterebbe spento per sempre, senza
    /// che nessuno se ne accorga (nessun client è in ascolto su quel
    /// percorso).
    /// </summary>
    [Fact]
    public async Task AncheSeLaGenerazioneEsplodeLEventoDiRitornoArriva()
    {
        var testo = new FakeAiTextProvider
        {
            ProssimoErrore = new InvalidOperationException("Guasto simulato del modello."),
        };
        var immagine = new FakeAiImageProvider();

        var engine = new FakeGameEngine(evt => evt switch
        {
            GameStartRequested => [new RequestIllustration(0, "un pinguino in doppiopetto")],
            _ => [],
        });

        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));

        var host = new GameHost(
            engine, rooms, null!, null!, CreaIllustrationRunner(testo, immagine), new ImageStore(), NullLogger<GameHost>.Instance);

        await host.DispatchAsync("STANZA", new GameStartRequested(Guid.NewGuid()));

        await AttendiCondizioneAsync(
            () => engine.EventiRicevuti.Any(e => e is IllustrationFinished),
            TimeSpan.FromSeconds(2));

        var esito = (IllustrationFinished)engine.EventiRicevuti.Last(e => e is IllustrationFinished);
        Assert.Null(esito.Path);
    }

    /// <summary>
    /// Senza il secondo catch di AvviaIllustrazione, l'HubException di una
    /// stanza sparita risalirebbe non osservata dal compito slegato: nessun
    /// client la vedrebbe comunque (non c'è nessuno in ascolto su una stanza
    /// che non esiste più), quindi il log è l'unica traccia osservabile che
    /// resta - ed è quello che questo test verifica.
    /// </summary>
    [Fact]
    public async Task SeLaStanzaESparitaSiLoggaESiTiraAvanti()
    {
        var testo = new FakeAiTextProvider("a penguin in a pinstripe suit");
        var immagine = new FakeAiImageProvider
        {
            Risposta = [1, 2, 3],
            Ritardo = TimeSpan.FromMilliseconds(200),
        };

        var engine = new FakeGameEngine(evt => evt switch
        {
            GameStartRequested => [new RequestIllustration(0, "un pinguino in doppiopetto")],
            _ => [],
        });

        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));

        var logger = new LoggerDiTest();
        var host = new GameHost(
            engine, rooms, null!, null!, CreaIllustrationRunner(testo, immagine), new ImageStore(), logger);

        await host.DispatchAsync("STANZA", new GameStartRequested(Guid.NewGuid()));

        // La stanza sparisce mentre la generazione è ancora in volo (riavvio
        // del server, tutti usciti: spec §7.1). Quando il compito slegato
        // rientra con DispatchAsync(IllustrationFinished), rooms.TryGet
        // fallisce.
        rooms.Remove("STANZA");

        await AttendiCondizioneAsync(
            () => logger.Voci.Any(v => v.Livello == LogLevel.Warning && v.Eccezione is HubException),
            TimeSpan.FromSeconds(2));

        // Il motore non deve mai vedere l'evento: la stanza non c'era più
        // quando è rientrato.
        Assert.DoesNotContain(engine.EventiRicevuti, e => e is IllustrationFinished);
    }

    /// <summary>
    /// I byte non viaggiano nell'evento: vanno nel deposito, e l'evento porta
    /// il percorso. Il motore non deve mai vedere un PNG (spec §5).
    /// </summary>
    [Fact]
    public async Task IByteFinisconoNelDepositoELEventoPortaIlPercorso()
    {
        var byteAttesi = new byte[] { 9, 9, 9, 9 };
        var testo = new FakeAiTextProvider("a penguin in a pinstripe suit");
        var immagine = new FakeAiImageProvider { Risposta = byteAttesi };

        var engine = new FakeGameEngine(evt => evt switch
        {
            GameStartRequested => [new RequestIllustration(0, "un pinguino in doppiopetto")],
            _ => [],
        });

        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));

        var deposito = new ImageStore();
        var host = new GameHost(
            engine, rooms, null!, null!, CreaIllustrationRunner(testo, immagine), deposito, NullLogger<GameHost>.Instance);

        await host.DispatchAsync("STANZA", new GameStartRequested(Guid.NewGuid()));

        await AttendiCondizioneAsync(
            () => engine.EventiRicevuti.Any(e => e is IllustrationFinished),
            TimeSpan.FromSeconds(2));

        var esito = (IllustrationFinished)engine.EventiRicevuti.Last(e => e is IllustrationFinished);
        Assert.NotNull(esito.Path);

        // Il percorso è il prefisso pubblico più l'identificativo: si toglie
        // il prefisso per interrogare il deposito con lo stesso identificativo
        // che l'endpoint HTTP riceverebbe.
        var id = esito.Path![ImageStore.Prefisso.Length..];

        Assert.True(deposito.TryGet(id, out var byteSalvati));
        Assert.Equal(byteAttesi, byteSalvati);
    }

    /// <summary>
    /// ImageStore.Salva torna null quando l'immagine da sola supera l'intero
    /// budget del deposito (vedi ImageStore.Salva): un salvataggio fallito va
    /// trattato come un fallimento della generazione, cioè
    /// IllustrationFinished(indice, null) — non un successo con un percorso
    /// che punterebbe al nulla. Se questo caso non venisse gestito, il server
    /// annuncerebbe a tutta la stanza un'immagine pronta a un indirizzo che
    /// non esiste.
    /// </summary>
    [Fact]
    public async Task UnSalvataggioFallitoDiventaEsitoNullo()
    {
        var byteImmagine = new byte[] { 1, 2, 3, 4, 5 };
        var testo = new FakeAiTextProvider("a penguin in a pinstripe suit");
        var immagine = new FakeAiImageProvider { Risposta = byteImmagine };

        var engine = new FakeGameEngine(evt => evt switch
        {
            GameStartRequested => [new RequestIllustration(0, "un pinguino in doppiopetto")],
            _ => [],
        });

        var rooms = new FakeRoomRegistry();
        rooms.Seed("STANZA", StanzaVuota("STANZA"));

        // Budget più piccolo dell'immagine stessa: Salva deve rifiutarla.
        var deposito = new ImageStore(budgetByte: byteImmagine.LongLength - 1);
        var host = new GameHost(
            engine, rooms, null!, null!, CreaIllustrationRunner(testo, immagine), deposito, NullLogger<GameHost>.Instance);

        await host.DispatchAsync("STANZA", new GameStartRequested(Guid.NewGuid()));

        await AttendiCondizioneAsync(
            () => engine.EventiRicevuti.Any(e => e is IllustrationFinished),
            TimeSpan.FromSeconds(2));

        var esito = (IllustrationFinished)engine.EventiRicevuti.Last(e => e is IllustrationFinished);
        Assert.Null(esito.Path);
    }
}
