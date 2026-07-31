using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class RevealTests
{
    private const int N = 3;
    private const int K = 3;

    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    private GameState PartitaConclusa()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < N; g++)
            {
                stato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}")).State;
            }
        }

        return stato;
    }

    [Fact]
    public void OgniAvanzamentoScopreUnaCasellaInPiu()
    {
        var stato = PartitaConclusa();

        var primo = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
        var passo = Assert.Single(primo.Broadcasts<RevealStepMessage>());

        Assert.Equal(0, passo.PhraseIndex);
        Assert.Equal(N, passo.TotalPhrases);
        Assert.Single(passo.RevealedSlots);
        Assert.False(passo.PhraseComplete);
    }

    /// <summary>
    /// Il voto è cieco (spec §3): durante il reveal il messaggio non porta
    /// affatto gli autori. Non è un campo vuoto da riempire più avanti — il
    /// campo non esiste, e questo test lo verifica dal lato osservabile:
    /// l'unica cosa che cambia a frase completa è PhraseComplete.
    /// </summary>
    [Fact]
    public void IlPassoDiRevealNonPortaMaiGliAutori()
    {
        var stato = PartitaConclusa();

        for (var i = 0; i < K - 1; i++)
        {
            var parziale = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
            var passo = Assert.Single(parziale.Broadcasts<RevealStepMessage>());

            Assert.False(passo.PhraseComplete);
            stato = parziale.State;
        }

        var ultimo = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
        var completo = Assert.Single(ultimo.Broadcasts<RevealStepMessage>());

        Assert.True(completo.PhraseComplete);
        Assert.Equal(K, completo.RevealedSlots.Count);
    }

    [Fact]
    public void DopoUnaFraseSiPassaAllaSuccessiva()
    {
        var stato = PartitaConclusa();

        for (var i = 0; i < K; i++)
        {
            stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        }

        var risultato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
        var passo = Assert.Single(risultato.Broadcasts<RevealStepMessage>());

        Assert.Equal(1, passo.PhraseIndex);
        Assert.Single(passo.RevealedSlots);
    }

    [Fact]
    public void SoloLHostPuoFarAvanzareIlReveal()
    {
        var stato = PartitaConclusa();

        var risultato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(1)));

        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("NOT_HOST", errore.Code);
        Assert.Empty(risultato.Broadcasts<RevealStepMessage>());
    }

    [Fact]
    public void ScopertaLUltimaFraseLaPartitaEConclusa()
    {
        var stato = PartitaConclusa();

        EngineResult risultato = null!;
        for (var i = 0; i < N * K; i++)
        {
            risultato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
            stato = risultato.State;
        }

        Assert.Equal(RoomPhase.Finished, stato.Phase);

        var finale = Assert.Single(risultato.Broadcasts<GameFinishedMessage>());
        Assert.Equal(N, finale.Results.Count);
        Assert.All(finale.Results, r => Assert.False(string.IsNullOrWhiteSpace(r.Text)));
    }

    /// <summary>
    /// Gli autori arrivano tutti insieme alla fine, ed è l'unico posto in cui
    /// arrivano: è ciò che rende cieco il voto che si inserirà qui in mezzo.
    /// </summary>
    [Fact]
    public void GliAutoriArrivanoSoloConIlMessaggioFinale()
    {
        var stato = PartitaConclusa();

        EngineResult risultato = null!;
        for (var i = 0; i < N * K; i++)
        {
            risultato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0)));
            stato = risultato.State;
        }

        var finale = Assert.Single(risultato.Broadcasts<GameFinishedMessage>());

        Assert.All(finale.Results, r => Assert.Equal(K, r.Authors.Count));
        Assert.All(finale.Results, r => Assert.All(r.Authors, a => Assert.StartsWith("G", a, StringComparison.Ordinal)));
    }
}
