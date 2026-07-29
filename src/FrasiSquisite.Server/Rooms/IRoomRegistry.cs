using FrasiSquisite.Domain.Model;

namespace FrasiSquisite.Server.Rooms;

/// <summary>
/// Le stanze attive vivono solo in memoria: un riavvio del server le perde, ed
/// è un limite accettato consapevolmente per la v1 (spec §7.1).
/// </summary>
public interface IRoomRegistry
{
    GameState Create();

    bool TryGet(string code, out GameState state);

    void Set(string code, GameState state);

    void Remove(string code);

    IReadOnlyCollection<string> Codes { get; }
}
