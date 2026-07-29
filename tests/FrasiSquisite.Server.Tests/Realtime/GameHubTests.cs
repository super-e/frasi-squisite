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

        // Cinque round con due giocatori.
        for (var round = 0; round < 5; round++)
        {
            await anna.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"anna{round}"));
            await bruno.Connection.InvokeAsync("SubmitSlot", new SubmitSlotRequest(codice, $"bruno{round}"));
        }

        await anna.Connection.InvokeAsync("AdvanceReveal", codice);
        await anna.WaitFor<RevealStepMessage>(TimeSpan.FromSeconds(5));

        var passo = anna.Last<RevealStepMessage>();
        Assert.Equal(0, passo.PhraseIndex);
        Assert.Equal(2, passo.TotalPhrases);
        Assert.Single(passo.RevealedSlots);
    }
}
