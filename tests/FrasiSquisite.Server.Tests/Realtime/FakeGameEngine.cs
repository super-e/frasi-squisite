using System.Collections.Concurrent;
using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Model;

namespace FrasiSquisite.Server.Tests.Realtime;

/// <summary>
/// Motore finto: non gioca nessuna partita vera, restituisce solo gli effetti
/// che il test gli dice di restituire per ciascun evento. Serve a isolare
/// <see cref="GameHost"/> dalla logica di dominio quando quello che si vuole
/// provare è l'adapter (lucchetto, rilancio in sottofondo, gestione degli
/// errori) e non le regole del gioco - quelle le coprono già i test del
/// motore in FrasiSquisite.Domain.Tests.
/// </summary>
public sealed class FakeGameEngine(Func<GameEvent, IReadOnlyList<Effect>> risposta) : IGameEngine
{
    private readonly ConcurrentQueue<GameEvent> _eventiRicevuti = new();

    public IReadOnlyCollection<GameEvent> EventiRicevuti => _eventiRicevuti;

    public EngineResult Handle(GameState state, GameEvent evt)
    {
        _eventiRicevuti.Enqueue(evt);
        return new EngineResult(state, risposta(evt));
    }
}
