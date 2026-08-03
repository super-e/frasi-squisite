using System.Collections.Concurrent;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Server.Rooms;

namespace FrasiSquisite.Server.Tests.Realtime;

/// <summary>
/// Registro delle stanze in memoria, senza le dipendenze del generatore di
/// codici e del catalogo schemi che servono a <see cref="RoomRegistry"/> vera:
/// nei test di <see cref="GameHost"/> lo stato della stanza non viene mai
/// letto, serve solo che "esista" o "non esista più" a comando (quest'ultimo
/// per simulare una stanza persa a metà di una rifinitura in volo).
/// </summary>
public sealed class FakeRoomRegistry : IRoomRegistry
{
    private readonly ConcurrentDictionary<string, GameState> _stanze = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Codes => _stanze.Keys.ToList();

    public GameState Create() => throw new NotSupportedException("Non serve nei test di GameHost: le stanze si seminano con Seed.");

    public bool TryGet(string code, out GameState state) => _stanze.TryGetValue(code, out state!);

    public void Set(string code, GameState state) => _stanze[code] = state;

    public void Remove(string code) => _stanze.TryRemove(code, out _);

    public void Seed(string code, GameState state) => _stanze[code] = state;
}
