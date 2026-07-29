using FrasiSquisite.Domain.Model;

namespace FrasiSquisite.Domain.Engine;

public interface IGameEngine
{
    EngineResult Handle(GameState state, GameEvent evt);
}
