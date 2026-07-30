using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Validation;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class BotTests
{
    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    private static Guid Bot(int i) => Guid.Parse($"11111111-0000-0000-0000-{i:D12}");

    /// <summary>
    /// Il test che dimostra il punto 1 del brief: senza il riempimento in
    /// StartGame, con un solo umano e un bot il round 0 non si completa mai
    /// (nessuno riempie la casella del bot) e la partita resta bloccata in
    /// scrittura per sempre. Solo una simulazione di partita intera lo scopre:
    /// i test dei singoli handler non lo vedrebbero.
    /// </summary>
    [Fact]
    public void UnaPartitaCompletaConUnUmanoEUnBotArrivaAlReveal()
    {
        const int k = 4;
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(k));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;
        stato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(0))).State;

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;
        Assert.Equal(RoomPhase.Writing, stato.Phase);

        for (var round = 0; round < k; round++)
        {
            stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(0), $"umano{round}")).State;
        }

        Assert.Equal(RoomPhase.Reveal, stato.Phase);
        Assert.All(stato.Phrases, f => Assert.True(f.IsComplete));
    }

    [Fact]
    public void AggiungereUnBotLoInserisceComeGiocatoreNonConnesso()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;

        var risultato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(0)));

        var bot = risultato.State.FindPlayer(Bot(0));
        Assert.NotNull(bot);
        Assert.True(bot.IsBot);
        Assert.False(bot.IsConnected);
        Assert.Equal("Bot Ada", bot.Nickname);
    }

    [Fact]
    public void SoloLHostPuoAggiungereUnBot()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(1), "Bruno")).State;

        var risultato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(1)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("NOT_HOST", errore.Code);
        Assert.DoesNotContain(risultato.State.Players, p => p.IsBot);
    }

    [Fact]
    public void NonSiPuoAggiungereUnBotFuoriDallaLobby()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(1), "Bruno")).State;
        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        var risultato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(0)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NOT_LOBBY", errore.Code);
    }

    [Fact]
    public void NonSiPuoSuperareMaxPlayersAggiungendoBot()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;

        for (var i = 0; i < GameEngine.MaxPlayers - 1; i++)
        {
            stato = _motore.Handle(stato, new BotAdded(Bot(i), Giocatore(0))).State;
        }

        Assert.Equal(GameEngine.MaxPlayers, stato.Players.Count);

        var risultato = _motore.Handle(stato, new BotAdded(Bot(99), Giocatore(0)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("ROOM_FULL", errore.Code);
        Assert.Equal(GameEngine.MaxPlayers, risultato.State.Players.Count);
    }

    [Fact]
    public void INomiAssegnatiSonoDistintiAncheAggiungendoERimuovendoBotInSequenza()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;

        stato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(0))).State;
        var nome0 = stato.FindPlayer(Bot(0))!.Nickname;

        stato = _motore.Handle(stato, new BotAdded(Bot(1), Giocatore(0))).State;
        var nome1 = stato.FindPlayer(Bot(1))!.Nickname;

        stato = _motore.Handle(stato, new BotRemoved(Bot(0), Giocatore(0))).State;

        stato = _motore.Handle(stato, new BotAdded(Bot(2), Giocatore(0))).State;
        var nome2 = stato.FindPlayer(Bot(2))!.Nickname;

        Assert.NotEqual(nome0, nome1);
        Assert.NotEqual(nome1, nome2);
    }

    [Fact]
    public void UnBotNonDiventaHostQuandoLHostEsce()
    {
        // Ordine scelto apposta: il bot ha JoinOrder minore di Bruno, così una
        // successione che non filtrasse su IsConnected sceglierebbe il bot.
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;
        stato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(0))).State;
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(1), "Bruno")).State;

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(0)));

        Assert.NotEqual(Bot(0), risultato.State.HostId);
        Assert.Equal(Giocatore(1), risultato.State.HostId);
    }

    [Fact]
    public void RinominareUnBotConNomeValidoLoApplicaNormalizzato()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;
        stato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(0))).State;

        var risultato = _motore.Handle(stato, new BotRenamed(Bot(0), "  Robo   Tino  ", Giocatore(0)));

        Assert.Equal("Robo Tino", risultato.State.FindPlayer(Bot(0))!.Nickname);
    }

    [Fact]
    public void RinominareUnBotConNomeNonValidoVieneRifiutato()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;
        stato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(0))).State;
        var nomeOriginale = stato.FindPlayer(Bot(0))!.Nickname;

        var risultato = _motore.Handle(stato, new BotRenamed(Bot(0), "   ", Giocatore(0)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("INVALID_NICKNAME", errore.Code);
        Assert.Equal(nomeOriginale, risultato.State.FindPlayer(Bot(0))!.Nickname);
    }

    [Fact]
    public void RinominareUnBotSenzaEssereHostVieneRifiutato()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(1), "Bruno")).State;
        stato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(0))).State;

        var risultato = _motore.Handle(stato, new BotRenamed(Bot(0), "Nuovo", Giocatore(1)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("NOT_HOST", errore.Code);
    }

    [Fact]
    public void RimuovereUnBotLoTogliDaiGiocatori()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;
        stato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(0))).State;

        var risultato = _motore.Handle(stato, new BotRemoved(Bot(0), Giocatore(0)));

        Assert.Null(risultato.State.FindPlayer(Bot(0)));
        Assert.Single(risultato.State.Players);
    }

    [Fact]
    public void RimuovereUnUmanoConRemoveBotVieneRifiutatoConNotABot()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(1), "Bruno")).State;

        var risultato = _motore.Handle(stato, new BotRemoved(Giocatore(1), Giocatore(0)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NOT_A_BOT", errore.Code);
        Assert.NotNull(risultato.State.FindPlayer(Giocatore(1)));
    }

    [Fact]
    public void RimuovereUnIdInesistenteRestituisceNoSuchPlayer()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;

        var risultato = _motore.Handle(stato, new BotRemoved(Guid.NewGuid(), Giocatore(0)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NO_SUCH_PLAYER", errore.Code);
    }

    [Fact]
    public void LeCaselleRiempiteDalBotPassanoLaValidazioneDelTesto()
    {
        const int k = 3;
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(k));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "Anna")).State;
        stato = _motore.Handle(stato, new BotAdded(Bot(0), Giocatore(0))).State;
        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        var testoBot = stato.Phrases[1].Slots[0]!.Text;

        var esito = SlotTextValidator.Validate(testoBot);
        Assert.True(esito.IsValid);
    }
}
