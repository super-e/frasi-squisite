using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class RientroTests
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
    public void IlRientroRimetteIsConnectedAVero()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        Assert.True(risultato.State.FindPlayer(Giocatore(1))!.IsConnected);
    }

    [Fact]
    public void IlRientroInScritturaMandaLaCasellaCorrenteSoloAChiRientra()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        var richiesta = Assert.Single(risultato.MessagesTo<SlotRequestMessage>(Giocatore(1)));
        Assert.Equal(0, richiesta.Round);
        Assert.Equal("Ruolo0", richiesta.Ruolo);
        Assert.Empty(risultato.MessagesTo<SlotRequestMessage>(Giocatore(0)));
        Assert.Empty(risultato.MessagesTo<SlotRequestMessage>(Giocatore(2)));
    }

    [Fact]
    public void IlRientroDopoIlPeriodoDiGraziaSegnalaLaCasellaGiaRiempita()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        // Il periodo di grazia è già scaduto: PlayerLeft ha marcato
        // Giocatore(1) disconnesso E FillDisconnected ha già riempito la
        // sua casella del round corrente con un bot.
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        var richiesta = Assert.Single(risultato.MessagesTo<SlotRequestMessage>(Giocatore(1)));
        Assert.True(richiesta.GiaInviato);
    }

    [Fact]
    public void IlRientroDuplicatoNonCambiaNulla()
    {
        var stato = PartitaAvviata(n: 3, k: 5);
        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;
        stato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        Assert.True(risultato.State.FindPlayer(Giocatore(1))!.IsConnected);
        Assert.Empty(risultato.MessagesTo<SlotRequestMessage>(Giocatore(1)));
    }

    [Fact]
    public void IlRientroDiUnGiocatoreInesistenteNonProduceEffetti()
    {
        var stato = PartitaAvviata(n: 3, k: 5);

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(99)));

        Assert.Empty(risultato.Effects);
        Assert.Same(stato, risultato.State);
    }

    [Fact]
    public void IlRientroInRifinituraMandaSoloLoStatoStanza()
    {
        var stato = PartitaAvviata(n: 3, k: 2);
        for (var round = 0; round < 2; round++)
        {
            for (var g = 0; g < 3; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }
        Assert.Equal(RoomPhase.Refining, stato.Phase);

        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        Assert.Single(risultato.Effects);
        Assert.IsType<BroadcastToRoom>(risultato.Effects[0]);
    }

    [Fact]
    public void IlRientroInRevealMandaIFrammentiCorrenti()
    {
        var stato = PartitaAvviata(n: 3, k: 3);
        for (var round = 0; round < 3; round++)
        {
            for (var g = 0; g < 3; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }
        stato = _motore.Handle(stato, new RefinementFinished(null)).State;
        stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        Assert.Equal(RoomPhase.Reveal, stato.Phase);

        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        var passo = Assert.Single(risultato.MessagesTo<RevealStepMessage>(Giocatore(1)));
        Assert.Equal(0, passo.PhraseIndex);
        Assert.False(passo.PhraseComplete);
    }

    [Fact]
    public void IlRientroInVotoMandaLeFrasiComposte()
    {
        var stato = PartitaAvviata(n: 3, k: 3);
        for (var round = 0; round < 3; round++)
        {
            for (var g = 0; g < 3; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }
        stato = _motore.Handle(stato, new RefinementFinished(null)).State;
        for (var i = 0; i < 3 * 3; i++)
        {
            stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        }
        Assert.Equal(RoomPhase.Voting, stato.Phase);

        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        var richiesta = Assert.Single(risultato.MessagesTo<VoteRequestMessage>(Giocatore(1)));
        Assert.Equal(3, richiesta.Phrases.Count);
    }

    [Fact]
    public void IlRientroAPartitaConclusaMandaLaClassifica()
    {
        var stato = PartitaAvviata(n: 3, k: 3);
        for (var round = 0; round < 3; round++)
        {
            for (var g = 0; g < 3; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }
        stato = _motore.Handle(stato, new RefinementFinished(null)).State;
        for (var i = 0; i < 3 * 3; i++)
        {
            stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        }
        for (var g = 0; g < 3; g++)
        {
            stato = _motore.Handle(stato, new VoteCast(Giocatore(g), 0)).State;
        }
        Assert.Equal(RoomPhase.Finished, stato.Phase);

        stato = _motore.Handle(stato, new PlayerLeft(Giocatore(1))).State;

        var risultato = _motore.Handle(stato, new PlayerRejoined(Giocatore(1)));

        var finale = Assert.Single(risultato.MessagesTo<GameFinishedMessage>(Giocatore(1)));
        Assert.Equal(3, finale.Results.Count);
    }
}
