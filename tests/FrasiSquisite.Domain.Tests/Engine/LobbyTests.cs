using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class LobbyTests
{
    private static readonly Guid Anna = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bruno = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Carla = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly IGameEngine _motore = new GameEngine(new RoleSchemaMode());

    private GameState StanzaVuota() => GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));

    [Fact]
    public void IlPrimoGiocatoreCheEntraDiventaHost()
    {
        var risultato = _motore.Handle(StanzaVuota(), new PlayerJoined(Anna, "Anna"));

        Assert.Equal(Anna, risultato.State.HostId);
        Assert.Single(risultato.State.Players);
    }

    [Fact]
    public void EntrandoSiRicevonoLoStatoDellaStanzaETuttiLoVedono()
    {
        var risultato = _motore.Handle(StanzaVuota(), new PlayerJoined(Anna, "Anna"));

        var stato = Assert.Single(risultato.Broadcasts<RoomStateMessage>());
        Assert.Equal("ABCD", stato.RoomCode);
        Assert.Equal(nameof(RoomPhase.Lobby), stato.Phase);
        Assert.Equal("Anna", Assert.Single(stato.Players).Nickname);
        Assert.True(Assert.Single(stato.Players).IsHost);
    }

    [Fact]
    public void IlSecondoGiocatoreNonDiventaHost()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;

        var risultato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno"));

        Assert.Equal(Anna, risultato.State.HostId);
        Assert.Equal(2, risultato.State.Players.Count);
    }

    [Fact]
    public void RientrareConLoStessoIdNonDuplicaIlGiocatore()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;

        var risultato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna"));

        Assert.Single(risultato.State.Players);
    }

    [Fact]
    public void QuandoEsceLHostIlRuoloPassaAlPresenteDaPiuTempo()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Carla, "Carla")).State;

        var risultato = _motore.Handle(stato, new PlayerLeft(Anna));

        Assert.Equal(Bruno, risultato.State.HostId);
        Assert.Equal(2, risultato.State.Players.Count);
    }

    [Fact]
    public void QuandoEsceUnNonHostLHostNonCambia()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno")).State;

        var risultato = _motore.Handle(stato, new PlayerLeft(Bruno));

        Assert.Equal(Anna, risultato.State.HostId);
    }

    [Fact]
    public void SoloLHostPuoAvviareLaPartita()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno")).State;

        var risultato = _motore.Handle(stato, new GameStartRequested(Bruno));

        Assert.Equal(RoomPhase.Lobby, risultato.State.Phase);
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Bruno));
        Assert.Equal("NOT_HOST", errore.Code);
    }

    [Fact]
    public void NonSiPuoAvviareUnaPartitaConUnSoloGiocatore()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;

        var risultato = _motore.Handle(stato, new GameStartRequested(Anna));

        Assert.Equal(RoomPhase.Lobby, risultato.State.Phase);
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Anna));
        Assert.Equal("TOO_FEW_PLAYERS", errore.Code);
    }
}
