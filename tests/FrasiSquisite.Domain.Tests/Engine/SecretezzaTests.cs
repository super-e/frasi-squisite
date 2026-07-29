using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Engine;

/// <summary>
/// Il requisito centrale del gioco (spec §2.3): nessun giocatore deve poter
/// vedere il contenuto di una casella non ancora rivelata. Questi test lo
/// verificano ispezionando ogni singolo messaggio prodotto dal motore.
/// </summary>
public class SecretezzaTests
{
    private readonly IGameEngine _motore =
        new GameEngine(new RoleSchemaMode(), new StaticWordPool(), new SeededRandomSource(1));

    private static Guid Giocatore(int i) => Guid.Parse($"00000000-0000-0000-0000-{i:D12}");

    [Fact]
    public void NessunMessaggioDuranteLaScritturaContieneTestoScrittoDaAltri()
    {
        const int n = 4;
        const int k = 5;
        var testiSegreti = new List<string>();

        var stato = GameState.NewRoom("ABCD", TestSchemas.WithSlots(k));
        for (var i = 0; i < n; i++)
        {
            stato = _motore.Handle(stato, new PlayerJoined(Giocatore(i), $"G{i}")).State;
        }

        stato = _motore.Handle(stato, new GameStartRequested(Giocatore(0))).State;

        for (var round = 0; round < k; round++)
        {
            for (var g = 0; g < n; g++)
            {
                var segreto = $"SEGRETO-r{round}-g{g}";
                var faseCorrente = stato.Phase;
                var risultato = _motore.Handle(stato, new SlotSubmitted(Giocatore(g), segreto));
                stato = risultato.State;

                testiSegreti.Add(segreto);

                // Ogni messaggio emesso da un invio fatto durante la scrittura
                // non deve contenere nessuno dei testi inviati, incluso quello
                // appena scritto: la fuga tipica è un broadcast che lo
                // rimbalza. Si guarda la fase precedente alla chiamata, non
                // quella successiva, altrimenti l'invio che fa uscire
                // dalla scrittura (compreso l'ultimo, che passa a Reveal)
                // non verrebbe mai ispezionato.
                if (faseCorrente == RoomPhase.Writing)
                {
                    var serializzati = risultato.AllMessages()
                        .Select(m => System.Text.Json.JsonSerializer.Serialize(m))
                        .ToList();

                    foreach (var precedente in testiSegreti)
                    {
                        Assert.All(serializzati, s =>
                            Assert.DoesNotContain(precedente, s, StringComparison.Ordinal));
                    }
                }
            }
        }
    }
}
