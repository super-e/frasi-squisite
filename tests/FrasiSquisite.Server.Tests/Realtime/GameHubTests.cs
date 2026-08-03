using System.Collections.Concurrent;
using System.Text.Json;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Server.Ai;
using FrasiSquisite.Server.Rooms;
using FrasiSquisite.Server.Tests.Ai;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Schemas;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FrasiSquisite.Server.Tests.Realtime;

public sealed class GameHubTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private sealed class Client(HubConnection connection) : IAsyncDisposable
    {
        private readonly List<(string Type, JsonElement Payload)> _received = [];

        public HubConnection Connection { get; } = connection;

        public IReadOnlyList<(string Type, JsonElement Payload)> Received => _received;

        public void Listen() =>
            Connection.On<string, JsonElement>("ReceiveMessage", (type, payload) =>
            {
                lock (_received)
                {
                    _received.Add((type, payload));
                }
            });

        public T Last<T>() where T : class
        {
            lock (_received)
            {
                var nome = typeof(T).Name;
                for (var i = _received.Count - 1; i >= 0; i--)
                {
                    if (_received[i].Type == nome)
                    {
                        return _received[i].Payload.Deserialize<T>(ProtocolJson.Options)!;
                    }
                }
            }

            throw new InvalidOperationException($"Nessun messaggio di tipo {typeof(T).Name} ricevuto.");
        }

        public int CountOf<T>()
        {
            lock (_received)
            {
                var nome = typeof(T).Name;
                return _received.Count(m => m.Type == nome);
            }
        }

        public async Task WaitFor<T>(TimeSpan timeout)
        {
            var scadenza = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < scadenza)
            {
                lock (_received)
                {
                    if (_received.Any(m => m.Type == typeof(T).Name))
                    {
                        return;
                    }
                }

                await Task.Delay(20);
            }

            throw new TimeoutException($"Nessun {typeof(T).Name} entro {timeout}.");
        }

        public async Task WaitForCount<T>(int count, TimeSpan timeout)
        {
            var scadenza = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < scadenza)
            {
                if (CountOf<T>() >= count)
                {
                    return;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException($"Meno di {count} messaggi di tipo {typeof(T).Name} entro {timeout}.");
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    /// <summary>
    /// Cattura i log di livello Error+ dell'host, per verificare che un
    /// percorso dichiaratamente "silenzioso" (es. la disconnessione su una
    /// stanza sparita) non lasci comunque un'eccezione non gestita nei log
    /// del server: quella è l'unica traccia osservabile del comportamento,
    /// dato che il client non vede nulla in entrambi i casi.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> VociDiErrore { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, VociDiErrore);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string categoria, ConcurrentQueue<string> vociDiErrore) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Error)
                {
                    vociDiErrore.Enqueue($"[{categoria}] {formatter(state, exception)} :: {exception}");
                }
            }
        }
    }

    private Task<Client> ConnettiAsync() => ConnettiAsync(_factory);

    private static async Task<Client> ConnettiAsync(WebApplicationFactory<Program> factory)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "hubs/game"),
                options => options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
            .Build();

        var client = new Client(connection);
        client.Listen();
        await connection.StartAsync();
        return client;
    }

    [Fact]
    public async Task CreareUnaStanzaRestituisceUnCodiceEDiventaHost()
    {
        await using var anna = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom",
            new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));

        Assert.False(string.IsNullOrWhiteSpace(codice));

        await anna.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));
        var stato = anna.Last<RoomStateMessage>();

        Assert.Equal(codice, stato.RoomCode);
        Assert.True(Assert.Single(stato.Players).IsHost);
    }

    [Fact]
    public async Task UnaVersioneDiProtocolloSbagliataVieneRifiutata()
    {
        await using var anna = await ConnettiAsync();

        await Assert.ThrowsAsync<HubException>(() =>
            anna.Connection.InvokeAsync<string>(
                "CreateRoom",
                new CreateRoomRequest(ProtocolVersion.Current + 1, Guid.NewGuid(), "Anna")));
    }

    [Fact]
    public async Task DueClientGiocanoUnaPartitaFinoAlReveal()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        var annaId = Guid.NewGuid();
        var brunoId = Guid.NewGuid();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, annaId, "Anna"));

        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, brunoId, "Bruno", codice));

        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await anna.Connection.InvokeAsync("StartGame", new StartGameRequest(codice));
        await anna.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));
        await bruno.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));

        var richiesta = anna.Last<SlotRequestMessage>();

        // TotalRounds deve rispecchiare le caselle dello schema della stanza,
        // non un numero fisso: qui la stanza è appena creata, quindi lo schema
        // è il default. Il valore letterale (5, quando il default era il
        // surrealista) rendeva questo test d'integrazione un secondo posto in
        // cui aggiornare il conteggio a ogni cambio di default — cosa che
        // EmbeddedSchemaCatalogTests già copre da sola.
        var totalRounds = new EmbeddedSchemaCatalog().Get(Schema.DefaultId).SlotCount;
        Assert.Equal(totalRounds, richiesta.TotalRounds);

        // Bruno invia due volte nello stesso round: solo lui deve vedere
        // l'errore "già inviato" (spec §2.3: nessun giocatore vede i fatti
        // privati di un altro). Se SendToPlayer regredisse a un broadcast
        // sull'intera stanza, questo messaggio arriverebbe anche ad Anna.
        await bruno.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, "bruno0"));
        await bruno.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, "bruno0-bis"));
        await bruno.WaitFor<ErrorMessage>(TimeSpan.FromSeconds(5));
        var erroreBruno = bruno.Last<ErrorMessage>();
        Assert.Equal("ALREADY_SUBMITTED", erroreBruno.Code);

        // Un giro completo con due giocatori (il primo invio di Bruno vale per il round 0).
        await anna.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, "anna0"));
        for (var round = 1; round < totalRounds; round++)
        {
            await anna.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"anna{round}"));
            await bruno.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"bruno{round}"));
        }

        // Ogni giocatore deve ricevere esattamente una SlotRequestMessage a
        // round: se il routing per-giocatore regredisse a un broadcast di
        // stanza, ciascuno ne riceverebbe il doppio (la propria e quella
        // dell'altro), e nessuna asserzione sul contenuto lo rileverebbe
        // perché per lo schema di default il contenuto è identico per round.
        await anna.WaitForCount<SlotRequestMessage>(totalRounds, TimeSpan.FromSeconds(5));
        await bruno.WaitForCount<SlotRequestMessage>(totalRounds, TimeSpan.FromSeconds(5));
        Assert.Equal(totalRounds, anna.CountOf<SlotRequestMessage>());
        Assert.Equal(totalRounds, bruno.CountOf<SlotRequestMessage>());

        // E l'errore di Bruno non deve mai comparire nella casella di Anna.
        Assert.Equal(0, anna.CountOf<ErrorMessage>());

        // Prima che ci fosse la rifinitura, la fine dell'ultimo round faceva
        // scoprire la prima casella in modo sincrono, dentro lo stesso
        // DispatchAsync del SubmitSlot: un solo AdvanceReveal bastava sempre.
        // Ora in mezzo c'è la fase Refining, e l'uscita da quella fase arriva
        // da un compito slegato (GameHost.AvviaRifinitura) che rientra con
        // una SECONDA DispatchAsync quando riprende il lucchetto della
        // stanza - una latenza non nulla anche con l'AI disattivata, che
        // risponde comunque in modo asincrono. Quella fine produce comunque
        // una RevealStepMessage iniziale e vuota (nessuna casella ancora
        // scoperta), proprio per portare tutti sulla schermata di reveal
        // senza che nessuno debba aspettare l'host (spec C1) - ma se il primo
        // AdvanceReveal qui sotto arriva PRIMA che quella transizione sia
        // avvenuta, la stanza è ancora in Refining e la chiamata viene
        // respinta con NOT_REVEALING (un errore privato: nessuna
        // RevealStepMessage), perdendo quella casella. Si rilegge perciò il
        // conteggio a ogni giro invece di sparare una sola chiamata alla
        // cieca - lo stesso pattern di GiocaFinoAllaFineAsync più sotto: se
        // la chiamata viene respinta, la RevealStepMessage iniziale e vuota
        // fa comunque salire il conteggio, e il giro successivo la riprova
        // finché non arriva il vero passo.
        while (anna.CountOf<RevealStepMessage>() < 2)
        {
            var passiPrima = anna.CountOf<RevealStepMessage>();
            await anna.Connection.InvokeAsync("AdvanceReveal", codice);
            await anna.WaitForCount<RevealStepMessage>(passiPrima + 1, TimeSpan.FromSeconds(5));
        }

        var passo = anna.Last<RevealStepMessage>();
        Assert.Equal(0, passo.PhraseIndex);
        Assert.Equal(2, passo.TotalPhrases);
        Assert.Single(passo.RevealedSlots);
    }

    /// <summary>
    /// Un AdvanceReveal alla volta finché non arriva la VoteRequestMessage
    /// (spec §2.4: una casella scoperta per avanzamento). Rilegge il
    /// conteggio dei passi a ogni giro invece di sparare un numero fisso di
    /// chiamate: fra la fine dell'ultimo round e l'ingresso effettivo in
    /// Reveal c'è di mezzo la fase Refining, la cui uscita arriva da un
    /// compito slegato (GameHost.AvviaRifinitura) con una latenza non nulla
    /// anche ad AI disattivata. Una chiamata sparata mentre la stanza è
    /// ancora in Refining verrebbe respinta con NOT_REVEALING (un errore
    /// privato, nessuna RevealStepMessage) e quella casella andrebbe persa;
    /// qui invece, se la chiamata viene respinta, la RevealStepMessage
    /// iniziale e vuota che chiude comunque la rifinitura fa salire il
    /// conteggio lo stesso, e il giro successivo la riprova.
    /// </summary>
    private static async Task AvanzaRevealFinoAlVotoAsync(Client anna, string codice)
    {
        while (anna.CountOf<VoteRequestMessage>() == 0)
        {
            var passiPrima = anna.CountOf<RevealStepMessage>();
            await anna.Connection.InvokeAsync("AdvanceReveal", codice);
            await anna.WaitForCount<RevealStepMessage>(passiPrima + 1, TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// Porta la partita di anna e bruno fino alla GameFinishedMessage:
    /// avvio, tutti i round scritti, reveal avanzato fino in fondo, voto di
    /// entrambi (unici umani connessi, quindi bastano loro a chiudere la
    /// fase). Usata dai test del Lotto D ("Nuova partita" / "Torna alla
    /// lobby"), che hanno bisogno di una stanza già in
    /// <see cref="RoomPhase.Finished"/> prima di poter cominciare.
    /// </summary>
    private static async Task<int> GiocaFinoAllaFineAsync(Client anna, Client bruno, string codice)
    {
        await anna.Connection.InvokeAsync("StartGame", new StartGameRequest(codice));
        await anna.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));
        await bruno.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));

        var totalRounds = anna.Last<SlotRequestMessage>().TotalRounds;

        for (var round = 0; round < totalRounds; round++)
        {
            await anna.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"anna{round}"));
            await bruno.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"bruno{round}"));
        }

        await AvanzaRevealFinoAlVotoAsync(anna, codice);

        await anna.WaitFor<VoteRequestMessage>(TimeSpan.FromSeconds(5));

        // Votano solo gli umani connessi (qui anna e bruno, nessun bot): il
        // voto del secondo chiude la fase da sé e fa arrivare la
        // GameFinishedMessage. Quel voto può far comparire il messaggio
        // mentre la sua stessa InvokeAsync è ancora in volo (il server manda
        // tutti gli effetti prima che il metodo hub ritorni, stesso dettaglio
        // già noto per gli altri metodi di questo hub), quindi non si legge
        // nulla subito dopo l'InvokeAsync: si aspetta la GameFinishedMessage
        // con l'aiutante apposta, come sotto.
        await anna.Connection.InvokeAsync("CastVote", new CastVoteRequest(codice, 0));
        await bruno.Connection.InvokeAsync("CastVote", new CastVoteRequest(codice, 0));

        await anna.WaitFor<GameFinishedMessage>(TimeSpan.FromSeconds(5));

        return totalRounds;
    }

    [Fact]
    public async Task DopoLaFineLHostPuoIniziareSubitoUnaNuovaPartita()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Bruno", codice));
        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        var totalRounds = await GiocaFinoAllaFineAsync(anna, bruno, codice);

        // "Nuova partita": riparte subito, senza passare dalla lobby -
        // arriva dritta a una nuova SlotRequestMessage per il round 0
        // (lotto-d-brief.md).
        await anna.Connection.InvokeAsync("NewGame", new NewGameRequest(codice));

        await anna.WaitForCount<SlotRequestMessage>(totalRounds + 1, TimeSpan.FromSeconds(5));
        await bruno.WaitForCount<SlotRequestMessage>(totalRounds + 1, TimeSpan.FromSeconds(5));

        Assert.Equal(0, anna.Last<SlotRequestMessage>().Round);
        Assert.Equal(0, bruno.Last<SlotRequestMessage>().Round);
    }

    [Fact]
    public async Task DopoLaFineLHostPuoTornareAllaLobbyEDaLiRiavviareComeSempre()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Bruno", codice));
        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await GiocaFinoAllaFineAsync(anna, bruno, codice);

        var statiPrimaDiTornare = anna.CountOf<RoomStateMessage>();
        await anna.Connection.InvokeAsync("BackToLobby", new BackToLobbyRequest(codice));
        await anna.WaitForCount<RoomStateMessage>(statiPrimaDiTornare + 1, TimeSpan.FromSeconds(5));

        Assert.Equal(nameof(RoomPhase.Lobby), anna.Last<RoomStateMessage>().Phase);

        // Da qui GameStartRequested funziona come sempre (brief del lotto).
        var richiesteCaselePrima = anna.CountOf<SlotRequestMessage>();
        await anna.Connection.InvokeAsync("StartGame", new StartGameRequest(codice));
        await anna.WaitForCount<SlotRequestMessage>(richiesteCaselePrima + 1, TimeSpan.FromSeconds(5));
        Assert.Equal(0, anna.Last<SlotRequestMessage>().Round);
    }

    [Fact]
    public async Task UnNonHostCheProvaNewGameOBackToLobbyRiceveNotHost()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Bruno", codice));
        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await GiocaFinoAllaFineAsync(anna, bruno, codice);

        await bruno.Connection.InvokeAsync("NewGame", new NewGameRequest(codice));
        await bruno.WaitFor<ErrorMessage>(TimeSpan.FromSeconds(5));
        Assert.Equal("NOT_HOST", bruno.Last<ErrorMessage>().Code);

        var erroriPrima = bruno.CountOf<ErrorMessage>();
        await bruno.Connection.InvokeAsync("BackToLobby", new BackToLobbyRequest(codice));
        await bruno.WaitForCount<ErrorMessage>(erroriPrima + 1, TimeSpan.FromSeconds(5));
        Assert.Equal("NOT_HOST", bruno.Last<ErrorMessage>().Code);
    }

    [Fact]
    public async Task UnGiocatoreRifiutatoPerchePartitaAvviataNonRestaSottoscrittoAllaStanza()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();
        await using var carla = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));

        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Bruno", codice));
        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await anna.Connection.InvokeAsync("StartGame", new StartGameRequest(codice));
        await anna.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));

        // Carla prova a entrare a partita già avviata (spec I2): deve essere
        // rifiutata, e soprattutto non deve restare iscritta ai gruppi
        // SignalR della stanza. Prima del fix, EntraAsync veniva eseguito
        // prima del controllo di fase: chi veniva rifiutato restava comunque
        // nel gruppo e avrebbe ricevuto ogni messaggio successivo di una
        // partita a cui non partecipa.
        await Assert.ThrowsAsync<HubException>(() =>
            carla.Connection.InvokeAsync(
                "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Carla", codice)));

        // Un broadcast alla stanza avviene subito dopo (il progresso del
        // round): se Carla fosse rimasta iscritta lo riceverebbe.
        await anna.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, "anna"));
        await bruno.WaitFor<RoundProgressMessage>(TimeSpan.FromSeconds(5));

        Assert.Empty(carla.Received);
    }

    [Fact]
    public async Task AggiungereRinominareERimuovereUnBotFunzionaViaSignalR()
    {
        await using var anna = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        await anna.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await anna.Connection.InvokeAsync("AddBot", new AddBotRequest(codice));
        await anna.WaitForCount<RoomStateMessage>(2, TimeSpan.FromSeconds(5));

        var statoConBot = anna.Last<RoomStateMessage>();
        var bot = Assert.Single(statoConBot.Players, p => p.IsBot);
        Assert.False(bot.IsConnected);

        await anna.Connection.InvokeAsync("RenameBot", new RenameBotRequest(codice, bot.Id, "Bot Ribattezzato"));
        await anna.WaitForCount<RoomStateMessage>(3, TimeSpan.FromSeconds(5));
        Assert.Equal("Bot Ribattezzato", Assert.Single(anna.Last<RoomStateMessage>().Players, p => p.IsBot).Nickname);

        await anna.Connection.InvokeAsync("RemoveBot", new RemoveBotRequest(codice, bot.Id));
        await anna.WaitForCount<RoomStateMessage>(4, TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(anna.Last<RoomStateMessage>().Players, p => p.IsBot);
    }

    /// <summary>
    /// I test sui bot dell'hub si fermavano alla lobby (aggiungi, rinomina,
    /// rimuovi) e quelli del dominio giocavano una partita col bot senza
    /// passare da SignalR: nessuno copriva la cucitura fra i due, che e'
    /// esattamente dove il gioco si e' bloccato in mano a un giocatore vero.
    ///
    /// Con un bot in stanza la casella del round 0 e' gia' riempita
    /// all'avvio, quindi l'invio dell'unico umano deve completare il round e
    /// far arrivare la richiesta del round 1.
    /// </summary>
    [Fact]
    public async Task UnaPartitaConUnBotAvanzaOltreIlPrimoRound()
    {
        await using var anna = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        await anna.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await anna.Connection.InvokeAsync("AddBot", new AddBotRequest(codice));
        await anna.WaitForCount<RoomStateMessage>(2, TimeSpan.FromSeconds(5));

        await anna.Connection.InvokeAsync("StartGame", new StartGameRequest(codice));
        await anna.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));
        Assert.Equal(0, anna.Last<SlotRequestMessage>().Round);

        await anna.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, "Il cadavere"));

        await anna.WaitForCount<SlotRequestMessage>(2, TimeSpan.FromSeconds(5));
        Assert.Equal(1, anna.Last<SlotRequestMessage>().Round);
    }

    [Fact]
    public async Task UnNonHostCheProvaAdAggiungereUnBotRiceveNotHost()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));

        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Bruno", codice));
        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await bruno.Connection.InvokeAsync("AddBot", new AddBotRequest(codice));
        await bruno.WaitFor<ErrorMessage>(TimeSpan.FromSeconds(5));

        Assert.Equal("NOT_HOST", bruno.Last<ErrorMessage>().Code);
    }

    [Fact]
    public async Task SetSchemaConUnIdInesistenteDaNoSuchSchema()
    {
        await using var anna = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        await anna.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        // L'id non esiste: il motore non deve nemmeno vedere l'evento (spec
        // del lotto), quindi non arriva un secondo RoomStateMessage - solo
        // l'errore al chiamante.
        await anna.Connection.InvokeAsync("SetSchema", new SetSchemaRequest(codice, "schema-che-non-esiste"));
        await anna.WaitFor<ErrorMessage>(TimeSpan.FromSeconds(5));

        Assert.Equal("NO_SUCH_SCHEMA", anna.Last<ErrorMessage>().Code);
        Assert.Equal(1, anna.CountOf<RoomStateMessage>());
    }

    [Fact]
    public async Task SetSchemaConUnIdValidoAggiornaLoStatoDiTutti()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));

        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Bruno", codice));
        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        var primoStato = anna.Last<RoomStateMessage>();
        Assert.Contains(primoStato.AvailableSchemas, s => s.Id == "proverbio");

        await anna.Connection.InvokeAsync("SetSchema", new SetSchemaRequest(codice, "proverbio"));

        // Deve arrivare a entrambi, non solo a chi ha fatto la richiesta.
        await anna.WaitForCount<RoomStateMessage>(2, TimeSpan.FromSeconds(5));
        await bruno.WaitForCount<RoomStateMessage>(2, TimeSpan.FromSeconds(5));

        Assert.Equal("proverbio", anna.Last<RoomStateMessage>().SchemaId);
        Assert.Equal(3, anna.Last<RoomStateMessage>().SlotCount);
        Assert.Equal("proverbio", bruno.Last<RoomStateMessage>().SchemaId);
        Assert.Equal(3, bruno.Last<RoomStateMessage>().SlotCount);
    }

    [Fact]
    public async Task SubmitSlotSuUnaStanzaInesistenteSegnalaErroreAlClient()
    {
        await using var anna = await ConnettiAsync();

        await anna.Connection.InvokeAsync<string>(
            "CreateRoom",
            new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        await anna.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        // Una stanza che non esiste (es. persa per un riavvio del server) deve
        // fallire in modo osservabile dal client, non completare come se il
        // comando fosse andato a buon fine (spec §7.1): altrimenti il client
        // resta bloccato in silenzio senza alcun modo di saperlo.
        await Assert.ThrowsAsync<HubException>(() =>
            anna.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest("STANZA-INESISTENTE", "testo")));
    }

    [Fact]
    public async Task DisconnessioneSuStanzaSparitaNonLasciaErroriNeiLogDelServer()
    {
        var loggerProvider = new CapturingLoggerProvider();
        await using var factory = _factory.WithWebHostBuilder(
            builder => Microsoft.AspNetCore.Hosting.WebHostBuilderExtensions.ConfigureLogging(
                builder, logging => logging.AddProvider(loggerProvider)));

        await using var anna = await ConnettiAsync(factory);

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom",
            new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));
        await anna.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        // La stanza sparisce mentre Anna è ancora connessa (es. riavvio del
        // server, spec §7.1): il registro è un singleton, quindi risolverlo
        // dal service provider dell'host di test dà l'istanza usata dall'hub.
        var registro = factory.Services.GetRequiredService<IRoomRegistry>();
        registro.Remove(codice);

        // Disconnettere Anna ora forza OnDisconnectedAsync a dispatchare
        // PlayerLeft su una stanza inesistente: DispatchAsync lancia
        // HubException. SignalR assorbe comunque l'eccezione per non far
        // esplodere la connessione (il client non vede nulla in entrambi i
        // casi), ma senza il try/catch in OnDisconnectedAsync quell'eccezione
        // non gestita compare nei log del server a ogni disconnessione di una
        // stanza sparita: è l'unica traccia osservabile del comportamento, e
        // ciò che questo test verifica non accada.
        await anna.Connection.StopAsync();

        // Attesa limitata di una condizione (non uno sleep alla cieca): dà il
        // tempo alla disconnessione lato server di completare, in modo che un
        // eventuale log di errore compaia prima dell'asserzione finale.
        var scadenza = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < scadenza && loggerProvider.VociDiErrore.IsEmpty)
        {
            await Task.Delay(20);
        }

        Assert.Empty(loggerProvider.VociDiErrore);
    }

    /// <summary>
    /// Due client giocano, scoprono tutto, votano, e ricevono la classifica.
    /// L'errore del doppio voto deve restare privato: se il routing per
    /// giocatore regredisse a un broadcast di stanza, arriverebbe a entrambi.
    /// </summary>
    [Fact]
    public async Task DueClientVotanoERicevonoLaClassifica()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        // Il requisito §8.5 del design generale: "il gioco e' interamente
        // giocabile senza AI", verificato e non sperato. Questo test gioca una
        // partita intera dalla lobby alla classifica, e l'asserzione qui dice
        // a quali condizioni: senza modello. Senza questa riga la garanzia
        // riposerebbe sul fatto che nessuno abbia configurato una chiave nei
        // test — cioe' su un caso, non su una scelta.
        Assert.IsType<DisabledAiTextProvider>(_factory.Services.GetRequiredService<IAiTextProvider>());

        var annaId = Guid.NewGuid();
        var brunoId = Guid.NewGuid();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, annaId, "Anna"));

        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, brunoId, "Bruno", codice));

        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await anna.Connection.InvokeAsync("StartGame", new StartGameRequest(codice));
        await anna.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));

        var totalRounds = new EmbeddedSchemaCatalog().Get(Schema.DefaultId).SlotCount;

        for (var round = 0; round < totalRounds; round++)
        {
            await anna.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"anna{round}"));
            await bruno.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"bruno{round}"));
        }

        // Due frasi da due caselle ciascuna... in realtà N frasi da
        // totalRounds caselle: il reveal chiede un passo per casella di ogni
        // frase, quindi 2 * totalRounds tocchi in tutto. Usa lo stesso
        // aiutante di GiocaFinoAllaFineAsync invece di sparare 2 * totalRounds
        // AdvanceReveal alla cieca: fra la fine dell'ultimo round e l'ingresso
        // in Reveal c'è di mezzo la fase Refining (uscita asincrona, latenza
        // non nulla anche ad AI disattivata), e una chiamata sparata mentre la
        // stanza è ancora in Refining verrebbe respinta con NOT_REVEALING,
        // perdendo quella casella e facendo mancare il reveal all'appuntamento
        // con la VoteRequestMessage.
        await AvanzaRevealFinoAlVotoAsync(anna, codice);

        await anna.WaitFor<VoteRequestMessage>(TimeSpan.FromSeconds(5));
        await bruno.WaitFor<VoteRequestMessage>(TimeSpan.FromSeconds(5));

        var daVotare = anna.Last<VoteRequestMessage>();
        Assert.Equal(2, daVotare.Phrases.Count);

        await anna.Connection.InvokeAsync("CastVote", new CastVoteRequest(codice, 0));
        await anna.Connection.InvokeAsync("CastVote", new CastVoteRequest(codice, 1));
        await anna.WaitFor<ErrorMessage>(TimeSpan.FromSeconds(5));
        Assert.Equal("ALREADY_VOTED", anna.Last<ErrorMessage>().Code);

        await bruno.Connection.InvokeAsync("CastVote", new CastVoteRequest(codice, 0));

        await anna.WaitFor<GameFinishedMessage>(TimeSpan.FromSeconds(5));
        await bruno.WaitFor<GameFinishedMessage>(TimeSpan.FromSeconds(5));

        var classifica = bruno.Last<GameFinishedMessage>();
        Assert.Equal(2, classifica.Results.Count);
        Assert.Equal(0, classifica.Results[0].PhraseIndex);
        Assert.Equal(2, classifica.Results[0].Votes);
        Assert.True(classifica.Results[0].IsWinner);

        Assert.Equal(0, bruno.CountOf<ErrorMessage>());
    }

    [Fact]
    public async Task SoloLHostPuoChiudereIlVotoDallHub()
    {
        await using var anna = await ConnettiAsync();
        await using var bruno = await ConnettiAsync();

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));

        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Bruno", codice));

        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await anna.Connection.InvokeAsync("StartGame", new StartGameRequest(codice));
        await anna.WaitFor<SlotRequestMessage>(TimeSpan.FromSeconds(5));

        var totalRounds = new EmbeddedSchemaCatalog().Get(Schema.DefaultId).SlotCount;

        for (var round = 0; round < totalRounds; round++)
        {
            await anna.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"anna{round}"));
            await bruno.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"bruno{round}"));
        }

        // Vedi il commento di AvanzaRevealFinoAlVotoAsync: sparare
        // 2 * totalRounds AdvanceReveal alla cieca rischia di perdere una
        // casella contro la fase Refining, che esce in modo asincrono.
        await AvanzaRevealFinoAlVotoAsync(anna, codice);

        await bruno.WaitFor<VoteRequestMessage>(TimeSpan.FromSeconds(5));

        await bruno.Connection.InvokeAsync("CloseVoting", new CloseVotingRequest(codice));

        await bruno.WaitFor<ErrorMessage>(TimeSpan.FromSeconds(5));
        Assert.Equal("NOT_HOST", bruno.Last<ErrorMessage>().Code);
        Assert.Equal(0, anna.CountOf<GameFinishedMessage>());
    }

    /// <summary>
    /// Partita fino alla classifica, l'host chiede l'illustrazione di una
    /// frase e riceve l'esito pronto (AI Task 5). Un provider finto è
    /// registrato al posto di quello disattivato di default nei test, così
    /// la generazione va a buon fine invece di fallire per assenza di
    /// configurazione: qui si prova la cucitura GameHub -> GameHost ->
    /// IllustrationRunner -> ImageStore, non il motore (già coperto da
    /// IllustrazioneTests in FrasiSquisite.Domain.Tests) né il runner (già
    /// coperto da IllustrationRunnerTests).
    /// </summary>
    [Fact]
    public async Task LHostChiedeLIllustrazioneEArrivaLImmaginePronta()
    {
        var testoFinto = new FakeAiTextProvider("a penguin in a pinstripe suit");
        var immagineFinta = new FakeAiImageProvider { Risposta = [1, 2, 3, 4] };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAiTextProvider>(testoFinto);
                services.AddSingleton<IAiImageProvider>(immagineFinta);
            }));

        await using var anna = await ConnettiAsync(factory);
        await using var bruno = await ConnettiAsync(factory);

        var codice = await anna.Connection.InvokeAsync<string>(
            "CreateRoom", new CreateRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Anna"));

        await bruno.Connection.InvokeAsync(
            "JoinRoom", new JoinRoomRequest(ProtocolVersion.Current, Guid.NewGuid(), "Bruno", codice));
        await bruno.WaitFor<RoomStateMessage>(TimeSpan.FromSeconds(5));

        await GiocaFinoAllaFineAsync(anna, bruno, codice);

        await anna.Connection.InvokeAsync("RequestIllustration", new RequestIllustrationRequest(codice, 0));

        await anna.WaitFor<IllustrationReadyMessage>(TimeSpan.FromSeconds(5));

        var pronta = anna.Last<IllustrationReadyMessage>();
        Assert.Equal(0, pronta.PhraseIndex);
        Assert.False(string.IsNullOrWhiteSpace(pronta.Path));
    }
}
