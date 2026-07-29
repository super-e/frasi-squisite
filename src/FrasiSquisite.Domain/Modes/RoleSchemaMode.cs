using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Modes;

/// <summary>
/// N frasi in parallelo, K round. Al round r il giocatore p riempie la casella
/// r della frase (p + r) mod N (spec §2.2).
/// </summary>
public sealed class RoleSchemaMode : IGameMode
{
    public string Id => "role-schema";

    public int PhraseCount(int playerCount, Schema schema) => playerCount;

    public SlotAssignment AssignSlot(int round, int playerIndex, int playerCount, Schema schema)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(round);
        ArgumentOutOfRangeException.ThrowIfNegative(playerIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(playerCount, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(playerIndex, playerCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(round, schema.SlotCount);

        return new SlotAssignment((playerIndex + round) % playerCount, round);
    }

    public bool IsComplete(GameState state) => state.Round >= state.Schema.SlotCount;
}
