using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.App.Tests.Services;

public class FakeGameConnectionTests
{
    [Fact]
    public async Task RegistraLeChiamateEffettuate()
    {
        var connessione = new FakeGameConnection();

        await connessione.ConnectAsync("http://localhost:5000");
        await connessione.CreateRoomAsync(Guid.NewGuid(), "Anna");

        Assert.Equal(["Connect(http://localhost:5000)", "CreateRoom(Anna)"], connessione.Calls);
        Assert.True(connessione.IsConnected);
    }

    [Fact]
    public void EmetteIMessaggiVersoGliIscritti()
    {
        var connessione = new FakeGameConnection();
        object? ricevuto = null;
        connessione.MessageReceived += m => ricevuto = m;

        var atteso = new RoundProgressMessage(0, 1, 3);
        connessione.Emit(atteso);

        Assert.Same(atteso, ricevuto);
    }
}
