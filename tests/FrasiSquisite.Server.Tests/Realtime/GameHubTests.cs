using System.Text.Json;
using FrasiSquisite.Shared.Protocol;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
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

    private async Task<Client> ConnettiAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_factory.Server.BaseAddress, "hubs/game"),
                options => options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
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
        Assert.Equal(5, richiesta.TotalRounds);
        var totalRounds = richiesta.TotalRounds;

        // Bruno invia due volte nello stesso round: solo lui deve vedere
        // l'errore "già inviato" (spec §2.3: nessun giocatore vede i fatti
        // privati di un altro). Se SendToPlayer regredisse a un broadcast
        // sull'intera stanza, questo messaggio arriverebbe anche ad Anna.
        await bruno.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, "bruno0"));
        await bruno.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, "bruno0-bis"));
        await bruno.WaitFor<ErrorMessage>(TimeSpan.FromSeconds(5));
        var erroreBruno = bruno.Last<ErrorMessage>();
        Assert.Equal("ALREADY_SUBMITTED", erroreBruno.Code);

        // Cinque round con due giocatori (il primo invio di Bruno vale per il round 0).
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

        await anna.Connection.InvokeAsync("AdvanceReveal", codice);
        await anna.WaitFor<RevealStepMessage>(TimeSpan.FromSeconds(5));

        var passo = anna.Last<RevealStepMessage>();
        Assert.Equal(0, passo.PhraseIndex);
        Assert.Equal(2, passo.TotalPhrases);
        Assert.Single(passo.RevealedSlots);
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
}
