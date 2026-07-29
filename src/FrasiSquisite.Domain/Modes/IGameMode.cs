using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Modes;

/// <summary>
/// La logica va scritta comunque: dietro un'interfaccia costa quasi nulla e fa
/// sì che la variante "frase a catena" diventi in futuro una classe nuova
/// invece di una riscrittura del motore (spec §3.4).
/// </summary>
public interface IGameMode
{
    string Id { get; }

    int PhraseCount(int playerCount, Schema schema);

    SlotAssignment AssignSlot(int round, int playerIndex, int playerCount, Schema schema);

    bool IsComplete(GameState state);
}
