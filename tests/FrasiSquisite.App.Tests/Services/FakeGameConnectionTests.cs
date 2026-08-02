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

    [Fact]
    public void EmetteLInterruzioneDiConnessioneVersoGliIscritti()
    {
        var connessione = new FakeGameConnection();
        var sollevato = false;
        connessione.ConnectionInterrupted += () => sollevato = true;

        connessione.EmitConnectionInterrupted();

        Assert.True(sollevato);
    }

    [Fact]
    public async Task NextFailureLanciaUnaVoltaSolaEPoiSiAzzera()
    {
        var connessione = new FakeGameConnection { NextFailure = new InvalidOperationException("boom") };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => connessione.CreateRoomAsync(Guid.NewGuid(), "Anna"));

        var codice = await connessione.CreateRoomAsync(Guid.NewGuid(), "Anna");

        Assert.Equal(connessione.NextRoomCode, codice);
    }

    /// <summary>
    /// L'aggancio esiste già per SubmitSlotAsync, aggiunto per riprodurre il
    /// blocco su "in attesa". Il voto ha la stessa forma — l'ultimo votante
    /// riceve la chiusura mentre la sua await è ancora in volo — quindi
    /// serve anche qui, altrimenti quel caso non è esprimibile come test.
    /// </summary>
    [Fact]
    public async Task IlMessaggioDuranteVotoArrivaPrimaCheLaChiamataRitorni()
    {
        var conn = new FakeGameConnection
        {
            MessaggioDuranteVoto = new GameFinishedMessage([]),
        };

        object? ricevuto = null;
        var giaRitornata = false;
        conn.MessageReceived += m =>
        {
            ricevuto = m;
            Assert.False(giaRitornata, "il messaggio è arrivato dopo il ritorno della chiamata");
        };

        await conn.CastVoteAsync("ABCD", 0);
        giaRitornata = true;

        Assert.IsType<GameFinishedMessage>(ricevuto);
    }

    [Fact]
    public async Task IlMessaggioDuranteVotoSiAzzeraDopoLUso()
    {
        var conn = new FakeGameConnection { MessaggioDuranteVoto = new GameFinishedMessage([]) };

        var conteggio = 0;
        conn.MessageReceived += _ => conteggio++;

        await conn.CastVoteAsync("ABCD", 0);
        await conn.CastVoteAsync("ABCD", 0);

        Assert.Equal(1, conteggio);
    }
}
