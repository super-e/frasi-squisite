using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class AbbandonoTests
{
    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    private GameState PartitaAvviata(int n, int k)
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(k));

        for (var i = 0; i < n; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        return _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;
    }

    [Fact]
    public void InLobbyUscireRimuoveIlGiocatore()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "G0")).State;
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(1), "G1")).State;

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(1)));

        Assert.Single(risultato.State.Players);
    }

    [Fact]
    public void InPartitaUscireNonRimuoveIlGiocatoreMaLoMarcaDisconnesso()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(1)));

        Assert.Equal(3, risultato.State.Players.Count);
        Assert.False(risultato.State.FindPlayer(Giocatore(1))!.IsConnected);
    }

    [Fact]
    public void IlNumeroDiFrasiNonCambiaQuandoQualcunoAbbandona()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(1)));

        Assert.Equal(3, risultato.State.Phrases.Count);
    }

    [Fact]
    public void LaCasellaDiChiAbbandonaVieneRiempitaDalBot()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        // Round 0, giocatore 1 -> frase (1 + 0) % 3 = 1, casella 0.
        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(1)));

        var slot = risultato.State.Phrases[1].Slots[0];
        Assert.NotNull(slot);
        Assert.False(string.IsNullOrWhiteSpace(slot.Text));
        Assert.Equal(Giocatore(1), slot.AuthorId);
    }

    [Fact]
    public void ChiAbbandonaNonBloccaLAvanzamentoDelRound()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(0), "uno")).State;
        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(2), "due"));

        Assert.Equal(1, risultato.State.Round);
    }

    [Fact]
    public void IlBotRiempieAncheINuoviRoundSenzaCheNessunoAspetti()
    {
        const int n = 3;
        const int k = 4;
        var stato = PartitaAvviata(n, k);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        for (var round = 0; round < k; round++)
        {
            foreach (var g in (int[])[0, 2])
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"r{round}g{g}")).State;
            }
        }

        Assert.Equal(RoomPhase.Reveal, stato.Phase);
        Assert.All(stato.Phrases, f => Assert.True(f.IsComplete));
    }

    [Fact]
    public void ConDueGiocatoriLUscitaDiUnoNonFaEsplodereIlMotore()
    {
        var stato = PartitaAvviata(n: 2, k: 3);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(0), "sopravvissuto"));

        Assert.Equal(1, risultato.State.Round);
        Assert.Empty(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
    }

    [Fact]
    public void SeAbbandonaLHostIlRuoloPassaAUnConnesso()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(0)));

        Assert.NotEqual(Giocatore(0), risultato.State.HostId);
        Assert.True(risultato.State.FindPlayer(risultato.State.HostId)!.IsConnected);
    }

    [Fact]
    public void ChiEGiaDisconnessoNonVieneRiempitoDueVolte()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;
        var testoBot = stato.Phrases[1].Slots[0]!.Text;

        var risultato = _motore.Handle(stato, new PlayerLeft(Giocatore(1)));

        Assert.Equal(testoBot, risultato.State.Phrases[1].Slots[0]!.Text);
    }
}
