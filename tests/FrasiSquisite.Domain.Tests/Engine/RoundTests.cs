using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class RoundTests
{
    private readonly IGameEngine _motore = new GameEngine(new RoleSchemaMode());

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    /// <summary>Crea una stanza con n giocatori e la porta in partita.</summary>
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
    public void AllAvvioSiCreanoNFrasiVuote()
    {
        var stato = PartitaAvviata(n: 4, k: 5);

        Assert.Equal(RoomPhase.Writing, stato.Phase);
        Assert.Equal(4, stato.Phrases.Count);
        Assert.All(stato.Phrases, f => Assert.Equal(5, f.Slots.Count));
        Assert.All(stato.Phrases, f => Assert.All(f.Slots, Assert.Null));
    }

    [Fact]
    public void AllAvvioOgniGiocatoreRiceveLaPropriaRichiestaDiCasella()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(5));
        for (var i = 0; i < 3; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        var risultato = _motore.Handle(stato, new GameStartRequested(Giocatore(0)));

        for (var i = 0; i < 3; i++)
        {
            var richiesta = Assert.Single(risultato.MessagesTo<SlotRequestMessage>(Giocatore(i)));
            Assert.Equal(0, richiesta.Round);
            Assert.Equal(5, richiesta.TotalRounds);
            Assert.Equal("Ruolo0", richiesta.Ruolo);
        }
    }

    [Fact]
    public void InviareUnaCasellaLaRegistraSullaFraseAssegnata()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        // Round 0, giocatore 1 → frase (1 + 0) % 3 = 1, casella 0.
        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "Il cadavere"));

        var slot = risultato.State.Phrases[1].Slots[0];
        Assert.NotNull(slot);
        Assert.Equal("Il cadavere", slot.Text);
        Assert.Equal(Giocatore(1), slot.AuthorId);
    }

    [Fact]
    public void IlTestoInviatoVieneNormalizzato()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "  il   vino  "));

        Assert.Equal("il vino", risultato.State.Phrases[1].Slots[0]!.Text);
    }

    [Fact]
    public void UnTestoNonValidoVieneRifiutatoConErrore()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "   "));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("INVALID_TEXT", errore.Code);
        Assert.All(risultato.State.Phrases, f => Assert.All(f.Slots, Assert.Null));
    }

    [Fact]
    public void InviareDueVolteNelloStessoRoundVieneRifiutato()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "primo")).State;

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "secondo"));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("ALREADY_SUBMITTED", errore.Code);
        Assert.Equal("primo", risultato.State.Phrases[1].Slots[0]!.Text);
    }

    [Fact]
    public void DopoOgniInvioTuttiVedonoIlProgressoDelRound()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(0), "uno"));

        var progresso = Assert.Single(risultato.Broadcasts<RoundProgressMessage>());
        Assert.Equal(0, progresso.Round);
        Assert.Equal(1, progresso.Submitted);
        Assert.Equal(3, progresso.Total);
    }

    [Fact]
    public void QuandoTuttiHannoInviatoSiPassaAlRoundSuccessivo()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(0), "uno")).State;
        stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(1), "due")).State;

        var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(2), "tre"));

        Assert.Equal(1, risultato.State.Round);
        Assert.Empty(risultato.State.SubmittedThisRound);

        for (var i = 0; i < 3; i++)
        {
            var richiesta = Assert.Single(risultato.MessagesTo<SlotRequestMessage>(Giocatore(i)));
            Assert.Equal(1, richiesta.Round);
            Assert.Equal("Ruolo1", richiesta.Ruolo);
        }
    }

    [Fact]
    public void DopoLUltimoRoundSiEntraInReveal()
    {
        var stato = PartitaAvviata(n: 3, k: 3);

        for (var round = 0; round < 3; round++)
        {
            for (var g = 0; g < 3; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"r{round}g{g}")).State;
            }
        }

        Assert.Equal(RoomPhase.Reveal, stato.Phase);
        Assert.All(stato.Phrases, f => Assert.True(f.IsComplete));
    }

    [Fact]
    public void UnaPartitaCompletaRiempieOgniCasellaDiOgniFrase()
    {
        const int n = 5;
        const int k = 4;
        var stato = PartitaAvviata(n, k);

        for (var round = 0; round < k; round++)
        {
            for (var g = 0; g < n; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"r{round}g{g}")).State;
            }
        }

        Assert.Equal(n, stato.Phrases.Count);
        Assert.All(stato.Phrases, f => Assert.All(f.Slots, s => Assert.NotNull(s)));
    }
}
