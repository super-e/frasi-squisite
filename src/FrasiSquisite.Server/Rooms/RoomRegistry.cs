using System.Collections.Concurrent;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Server.Rooms;

public sealed class RoomRegistry(RoomCodeGenerator codes, ISchemaCatalog schemas) : IRoomRegistry
{
    private const int MaxCodeAttempts = 100;

    private readonly ConcurrentDictionary<string, GameState> _rooms =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Codes => _rooms.Keys.ToList();

    public GameState Create()
    {
        var schema = schemas.Get(Schema.DefaultId);

        for (var tentativo = 0; tentativo < MaxCodeAttempts; tentativo++)
        {
            var codice = codes.Next();
            var stato = GameState.NewRoom(codice, schema);

            if (_rooms.TryAdd(codice, stato))
            {
                return stato;
            }
        }

        throw new InvalidOperationException(
            $"Impossibile generare un codice stanza libero dopo {MaxCodeAttempts} tentativi.");
    }

    public bool TryGet(string code, out GameState state) => _rooms.TryGetValue(code, out state!);

    public void Set(string code, GameState state) => _rooms[code] = state;

    public void Remove(string code) => _rooms.TryRemove(code, out _);
}
