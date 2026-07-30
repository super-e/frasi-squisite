using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

/// <summary>
/// Lotto C: <see cref="SchemaSelected"/> cambia lo schema della stanza in
/// lobby. Il motore riceve lo Schema già risolto (non un id): non conosce il
/// catalogo, per progetto (lotto-c-brief.md).
/// </summary>
public class SchemaSelectionTests
{
    private static readonly Guid Anna = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bruno = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    private GameState StanzaVuota() => GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));

    [Fact]
    public void LHostPuoCambiareLoSchemaInLobby()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        var nuovoSchema = TestSchemas.WithSlots(3);

        var risultato = _motore.Handle(stato, new SchemaSelected(nuovoSchema, Anna));

        Assert.Equal(nuovoSchema, risultato.State.Schema);
        var broadcast = Assert.Single(risultato.Broadcasts<RoomStateMessage>());
        Assert.Equal(nuovoSchema.Id, broadcast.SchemaId);
        Assert.Equal(3, broadcast.SlotCount);
    }

    [Fact]
    public void SoloLHostPuoCambiareLoSchema()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno")).State;
        var schemaOriginale = stato.Schema;

        var risultato = _motore.Handle(stato, new SchemaSelected(TestSchemas.WithSlots(3), Bruno));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Bruno));
        Assert.Equal("NOT_HOST", errore.Code);
        Assert.Equal(schemaOriginale, risultato.State.Schema);
    }

    [Fact]
    public void NonSiPuoCambiareLoSchemaFuoriDallaLobby()
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno")).State;
        stato = _motore.Handle(stato, new GameStartRequested(Anna)).State;
        var schemaOriginale = stato.Schema;

        var risultato = _motore.Handle(stato, new SchemaSelected(TestSchemas.WithSlots(3), Anna));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Anna));
        Assert.Equal("NOT_LOBBY", errore.Code);
        Assert.Equal(schemaOriginale, risultato.State.Schema);
    }

    /// <summary>
    /// In lobby non c'è nessuna partita in corso, quindi non ci sono frasi né
    /// round da azzerare: cambiare schema deve toccare solo il campo Schema.
    /// Le liste invariate restano le STESSE istanze (Assert.Same), non solo
    /// uguali: è la prova che nessun altro campo viene ricostruito.
    /// </summary>
    [Fact]
    public void CambiareSchemaInLobbyNonAlteraNientAltroDelloStato()
    {
        // AvailableSchemas popolato (non la lista vuota di default di
        // StanzaVuota): una IReadOnlyList<T> vuota da collection expression
        // si abbassa sempre alla stessa istanza cache Array.Empty<T>(), quindi
        // Assert.Same passerebbe anche se il motore ricostruisse una lista
        // vuota nuova - non proverebbe che il campo è rimasto intoccato.
        var schemiDisponibili = new[] { new SchemaView("test-5", "Test 5", 5) };
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5), schemiDisponibili);
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno")).State;
        stato = _motore.Handle(stato, new BotAdded(Giocatore(99), Anna)).State;

        var risultato = _motore.Handle(stato, new SchemaSelected(TestSchemas.WithSlots(3), Anna));
        var nuovo = risultato.State;

        Assert.Equal(stato.RoomCode, nuovo.RoomCode);
        Assert.Equal(stato.Phase, nuovo.Phase);
        Assert.Equal(stato.HostId, nuovo.HostId);
        Assert.Same(stato.Players, nuovo.Players);
        Assert.Same(stato.AvailableSchemas, nuovo.AvailableSchemas);
        Assert.Equal(stato.NextJoinOrder, nuovo.NextJoinOrder);
        Assert.Equal(stato.Round, nuovo.Round);
        Assert.Same(stato.Phrases, nuovo.Phrases);
        Assert.Same(stato.SubmittedThisRound, nuovo.SubmittedThisRound);
        Assert.Equal(stato.RevealPhraseIndex, nuovo.RevealPhraseIndex);
        Assert.Equal(stato.RevealSlotCount, nuovo.RevealSlotCount);
        Assert.NotEqual(stato.Schema, nuovo.Schema);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void UnaPartitaGiocataConUnoSchemaDaKCaselleFinisceInKRound(int k)
    {
        var stato = StanzaVuota();
        stato = _motore.Handle(stato, new PlayerJoined(Anna, "Anna")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Bruno, "Bruno")).State;
        stato = _motore.Handle(stato, new SchemaSelected(TestSchemas.WithSlots(k), Anna)).State;
        Assert.Equal(k, stato.Schema.SlotCount);

        stato = _motore.Handle(stato, new GameStartRequested(Anna)).State;
        Assert.Equal(RoomPhase.Writing, stato.Phase);

        for (var round = 0; round < k; round++)
        {
            stato = _motore.Handle(stato, new SlotSubmitted(Anna, $"a{round}")).State;
            stato = _motore.Handle(stato, new SlotSubmitted(Bruno, $"b{round}")).State;
        }

        Assert.Equal(RoomPhase.Reveal, stato.Phase);
        Assert.All(stato.Phrases, f => Assert.Equal(k, f.Slots.Count));
        Assert.All(stato.Phrases, f => Assert.True(f.IsComplete));
    }
}
