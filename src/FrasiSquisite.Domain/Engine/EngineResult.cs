using FrasiSquisite.Domain.Model;

namespace FrasiSquisite.Domain.Engine;

public sealed record EngineResult(GameState State, IReadOnlyList<Effect> Effects)
{
    public static EngineResult NoChange(GameState state) => new(state, []);
}
