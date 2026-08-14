using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

public class IllustrazioneTests
{
    private const int N = 3;
    private const int K = 3;

    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    /// <summary>Partita conclusa: tutti hanno votato, la classifica è arrivata.</summary>
    private GameState AllaClassifica()
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

        stato = _motore.Handle(stato, new RefinementFinished(null)).State;

        for (var i = 0; i < N * K; i++)
        {
            stato = _motore.Handle(stato, new RevealAdvanceRequested(Giocatore(0))).State;
        }

        for (var i = 0; i < N; i++)
        {
            stato = _motore.Handle(stato, new VoteCast(Giocatore(i), 0)).State;
        }

        return stato;
    }

    [Fact]
    public void LHostChiedeLIllustrazioneEIlMotoreEmetteLEffetto()
    {
        var risultato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1));

        var effetto = Assert.Single(risultato.Effects.OfType<RequestIllustration>());
        Assert.Equal(1, effetto.PhraseIndex);
        Assert.False(string.IsNullOrWhiteSpace(effetto.Frase));
    }

    /// <summary>
    /// L'effetto porta la frase COMPOSTA, non le caselle: chi genera l'immagine
    /// deve leggere una frase italiana, e ricomporla fuori dal motore vorrebbe
    /// dire duplicare Schema.Compose in un posto che non ha lo schema.
    /// </summary>
    [Fact]
    public void LEffettoPortaLaFraseComposta()
    {
        var stato = AllaClassifica();

        var risultato = _motore.Handle(stato, new IllustrationRequested(Giocatore(0), 1));

        var effetto = Assert.Single(risultato.Effects.OfType<RequestIllustration>());
        var attesa = stato.Schema.Compose([.. stato.Phrases[1].Slots.Select(s => s!.Text)]);
        Assert.Equal(attesa, effetto.Frase);
    }

    /// <summary>
    /// Un doppio tocco non paga due volte (spec §5). Ogni immagine costa circa
    /// nove centesimi: questa non è un'ottimizzazione, è la differenza fra un
    /// dito impaziente e il conto che raddoppia.
    /// </summary>
    [Fact]
    public void LaStessaFraseNonVieneChiestaDueVolte()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        var risultato = _motore.Handle(stato, new IllustrationRequested(Giocatore(0), 1));

        Assert.Empty(risultato.Effects.OfType<RequestIllustration>());
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("ILLUSTRATION_ALREADY_REQUESTED", errore.Code);
    }

    [Fact]
    public void UnAltraFraseSiPuoChiedereLoStesso()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        var risultato = _motore.Handle(stato, new IllustrationRequested(Giocatore(0), 0));

        Assert.Single(risultato.Effects.OfType<RequestIllustration>());
    }

    /// <summary>
    /// Il tetto è per partita conclusa (IllustrationsRequested si azzera a
    /// ogni nuova partita): con un tetto di 1, la seconda richiesta - anche
    /// su una frase diversa dalla prima - viene rifiutata (backlog.md §4,
    /// rilievo 7).
    /// </summary>
    [Fact]
    public void OltreIlTettoConfiguratoLeRichiesteVengonoRifiutate()
    {
        var motoreConTetto = new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1), massimoIllustrazioniPerStanza: 1);
        var statoConUna = motoreConTetto.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        var risultato = motoreConTetto.Handle(statoConUna, new IllustrationRequested(Giocatore(0), 0));

        Assert.Empty(risultato.Effects.OfType<RequestIllustration>());
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("ILLUSTRATION_LIMIT_REACHED", errore.Code);
    }

    /// <summary>
    /// Senza specificare il parametro, il comportamento resta quello di
    /// sempre: nessun tetto. AllaClassifica() con N = K = 3 produce 3
    /// frasi, indici 0-2: tre richieste sullo stesso stato, tutte accettate,
    /// nessun tetto di default a fermarle.
    /// </summary>
    [Fact]
    public void SenzaConfigurazioneNonCEUnTettoDiDefault()
    {
        var stato = AllaClassifica();
        for (var i = 0; i < 3; i++)
        {
            stato = _motore.Handle(stato, new IllustrationRequested(Giocatore(0), i)).State;
        }

        Assert.Equal(3, stato.IllustrationsRequested.Count);
    }

    /// <summary>
    /// Se nel frattempo la stanza è tornata in Lobby (nuova partita, o
    /// ritorno alla lobby), un esito di illustrazione arrivato in ritardo
    /// per la partita precedente non deve avere alcun effetto: non è un
    /// errore da segnalare a nessuno (nessun giocatore l'ha chiesto in
    /// questo momento), è solo un evento interno da ignorare
    /// (backlog.md §4, rilievo 3).
    /// </summary>
    [Fact]
    public void UnEsitoTardivoDopoIlRitornoInLobbyNonHaEffetto()
    {
        var chiesta = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;
        var nuovaPartita = _motore.Handle(chiesta, new BackToLobbyRequested(Giocatore(0))).State;

        var risultato = _motore.Handle(nuovaPartita, new IllustrationFinished(1, "/illustrazioni/tardiva"));

        Assert.Equal(nuovaPartita, risultato.State);
        Assert.Empty(risultato.Effects);
    }

    [Fact]
    public void SoloLHostPuoChiederla()
    {
        var risultato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(1), 0));

        Assert.Empty(risultato.Effects.OfType<RequestIllustration>());
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(1)));
        Assert.Equal("NOT_HOST", errore.Code);
    }

    [Fact]
    public void PrimaDellaClassificaNonSiPuoChiedere()
    {
        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(K));
        stato = _motore.Handle(stato, new PlayerJoined(Giocatore(0), "G0")).State;

        var risultato = _motore.Handle(stato, new IllustrationRequested(Giocatore(0), 0));

        Assert.Empty(risultato.Effects.OfType<RequestIllustration>());
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NOT_FINISHED", errore.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void UnIndiceFuoriDaiLimitiVieneRifiutato(int indice)
    {
        var risultato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), indice));

        Assert.Empty(risultato.Effects.OfType<RequestIllustration>());
        var errore = Assert.Single(risultato.MessagesTo<ErrorMessage>(Giocatore(0)));
        Assert.Equal("NO_SUCH_PHRASE", errore.Code);
    }

    [Fact]
    public void LIllustrazioneProntaVieneMandataATutti()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        var risultato = _motore.Handle(stato, new IllustrationFinished(1, "/illustrazioni/abc"));

        var messaggio = Assert.Single(risultato.Broadcasts<IllustrationReadyMessage>());
        Assert.Equal(1, messaggio.PhraseIndex);
        Assert.Equal("/illustrazioni/abc", messaggio.Path);
    }

    /// <summary>
    /// Il fallimento deve TOGLIERE la frase dall'insieme, o il pulsante
    /// resterebbe spento per sempre e l'host non potrebbe riprovare.
    /// </summary>
    [Fact]
    public void DopoUnFallimentoSiPuoRiprovare()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;
        var fallito = _motore.Handle(stato, new IllustrationFinished(1, null));

        Assert.Single(fallito.Broadcasts<IllustrationFailedMessage>());

        var riprova = _motore.Handle(fallito.State, new IllustrationRequested(Giocatore(0), 1));

        Assert.Single(riprova.Effects.OfType<RequestIllustration>());
    }

    /// <summary>
    /// Stessa guardia della rifinitura: un esito che arriva quando la stanza è
    /// già ripartita non deve toccare la partita nuova.
    /// </summary>
    [Fact]
    public void UnEsitoFuoriFaseVieneIgnorato()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;
        stato = _motore.Handle(stato, new NewGameRequested(Giocatore(0))).State;

        var risultato = _motore.Handle(stato, new IllustrationFinished(1, "/illustrazioni/abc"));

        Assert.Empty(risultato.Effects);
    }

    /// <summary>
    /// Qui la fase da sola non basta: a differenza della rifinitura, in
    /// <c>Finished</c> le richieste sono più di una e concorrenti su indici
    /// diversi, quindi un esito duplicato o tardivo per un indice che non è
    /// (più) in attesa deve essere ignorato, non ribroadcast a tutta la stanza.
    /// </summary>
    [Fact]
    public void UnEsitoPerUnIndiceMaiChiestoVieneIgnorato()
    {
        var stato = AllaClassifica();

        var risultato = _motore.Handle(stato, new IllustrationFinished(1, "/illustrazioni/abc"));

        Assert.Empty(risultato.Effects);
    }

    /// <summary>
    /// Stesso caso, ma dopo che l'indice è già stato tolto da un fallimento:
    /// un secondo esito tardivo per quello stesso indice non deve
    /// ribroadcastare né un successo né un altro fallimento.
    /// </summary>
    [Fact]
    public void UnEsitoTardivoDopoUnFallimentoVieneIgnorato()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;
        stato = _motore.Handle(stato, new IllustrationFinished(1, null)).State;

        var risultato = _motore.Handle(stato, new IllustrationFinished(1, "/illustrazioni/tardivo"));

        Assert.Empty(risultato.Effects);
    }

    [Fact]
    public void UnaPartitaNuovaAzzeraLeIllustrazioni()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        stato = _motore.Handle(stato, new NewGameRequested(Giocatore(0))).State;

        Assert.Empty(stato.IllustrationsRequested);
    }

    [Fact]
    public void IlRitornoInLobbyAzzeraLeIllustrazioni()
    {
        var stato = _motore.Handle(AllaClassifica(), new IllustrationRequested(Giocatore(0), 1)).State;

        stato = _motore.Handle(stato, new BackToLobbyRequested(Giocatore(0))).State;

        Assert.Empty(stato.IllustrationsRequested);
    }
}
