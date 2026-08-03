using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class RifinituraTests
{
    private const int N = 2;
    private const int K = 2;

    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    /// <summary>Partita con tutte le caselle scritte: e' il momento in cui si entra in Refining.</summary>
    private (GameState Stato, EngineResult Ultimo) ScritturaConclusa()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        EngineResult ultimo = null!;
        for (var round = 0; round < K; round++)
        {
            for (var g = 0; g < N; g++)
            {
                ultimo = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), $"p{round}{g}"));
                stato = ultimo.State;
            }
        }

        return (stato, ultimo);
    }

    [Fact]
    public void FinitaLaScritturaSiEntraInRefiningENonInReveal()
    {
        var (stato, _) = ScritturaConclusa();

        Assert.Equal(RoomPhase.Refining, stato.Phase);
    }

    [Fact]
    public void EntrandoInRefiningVieneChiestaLaRifinitura()
    {
        var (_, ultimo) = ScritturaConclusa();

        var richiesta = Assert.Single(ultimo.Effects.OfType<RequestRefinement>());

        Assert.Equal(N, richiesta.Frasi.Count);
        Assert.All(richiesta.Frasi, f => Assert.Equal(K, f.Count));
        Assert.False(string.IsNullOrWhiteSpace(richiesta.Template));
    }

    [Fact]
    public void LaRifinituraRiuscitaSostituisceIlTestoEPortaAlReveal()
    {
        var (stato, _) = ScritturaConclusa();

        var rifinite = stato.Phrases
            .Select(f => (IReadOnlyList<string>)[.. f.Slots.Select(s => "con " + s!.Text)])
            .ToList();

        var risultato = _motore.Handle(stato, new RefinementFinished(rifinite));

        Assert.Equal(RoomPhase.Reveal, risultato.State.Phase);
        Assert.Equal("con p00", risultato.State.Phrases[0].Slots[0]!.Text);
        Assert.Single(risultato.Broadcasts<RevealStepMessage>());
    }

    /// <summary>
    /// Gli autori non devono cambiare: la rifinitura tocca il testo, non chi
    /// l'ha scritto, e la classifica finale li mostra.
    /// </summary>
    [Fact]
    public void LaRifinituraNonTocccaGliAutori()
    {
        var (stato, _) = ScritturaConclusa();
        var autoriPrima = stato.Phrases[0].Slots.Select(s => s!.AuthorId).ToList();

        var rifinite = stato.Phrases
            .Select(f => (IReadOnlyList<string>)[.. f.Slots.Select(s => "con " + s!.Text)])
            .ToList();

        var risultato = _motore.Handle(stato, new RefinementFinished(rifinite));

        Assert.Equal(autoriPrima, risultato.State.Phrases[0].Slots.Select(s => s!.AuthorId));
    }

    [Fact]
    public void LaRifinituraFallitaPortaComunqueAlReveal()
    {
        var (stato, _) = ScritturaConclusa();

        var risultato = _motore.Handle(stato, new RefinementFinished(null));

        Assert.Equal(RoomPhase.Reveal, risultato.State.Phase);
        Assert.Equal("p00", risultato.State.Phrases[0].Slots[0]!.Text);
        Assert.Single(risultato.Broadcasts<RevealStepMessage>());
    }

    /// <summary>
    /// Una casella che il modello ha riscritto torna grezza, senza che le
    /// altre ne risentano: e' RefinementGuard, applicato dal motore.
    /// </summary>
    [Fact]
    public void UnaCasellaRiscrittaDalModelloTornaGrezza()
    {
        var (stato, _) = ScritturaConclusa();

        var rifinite = stato.Phrases
            .Select((f, i) => (IReadOnlyList<string>)[.. f.Slots.Select((s, j) =>
                i == 0 && j == 0 ? "tutt'altro" : "con " + s!.Text)])
            .ToList();

        var risultato = _motore.Handle(stato, new RefinementFinished(rifinite));

        Assert.Equal("p00", risultato.State.Phrases[0].Slots[0]!.Text);
        Assert.Equal("con p11", risultato.State.Phrases[0].Slots[1]!.Text);
    }

    /// <summary>
    /// Se la stanza e' uscita da Refining - l'host ha ricominciato, o e'
    /// tornato in lobby - applicare quelle caselle sovrascriverebbe una
    /// partita nuova con i resti di quella vecchia (spec §3).
    /// </summary>
    [Fact]
    public void UnaRifinituraInRitardoVieneIgnorata()
    {
        var (stato, _) = ScritturaConclusa();
        var inLobby = stato with { Phase = RoomPhase.Lobby };

        var risultato = _motore.Handle(inLobby, new RefinementFinished(null));

        Assert.Equal(RoomPhase.Lobby, risultato.State.Phase);
        Assert.Empty(risultato.Effects);
    }

    [Fact]
    public void TornareAllaLobbyAzzeraLaFaseDiRifinitura()
    {
        var (stato, _) = ScritturaConclusa();
        var finito = stato with { Phase = RoomPhase.Finished };

        var azzerato = _motore.Handle(finito, new BackToLobbyRequested(Giocatore(0))).State;

        Assert.Equal(RoomPhase.Lobby, azzerato.Phase);
    }
}
