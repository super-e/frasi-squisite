using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Model;

public sealed record GameState(
    string RoomCode,
    RoomPhase Phase,
    Guid HostId,
    IReadOnlyList<Player> Players,
    Schema Schema,
    long NextJoinOrder,
    int Round,
    IReadOnlyList<Phrase> Phrases,
    IReadOnlySet<Guid> SubmittedThisRound,
    int RevealPhraseIndex,
    int RevealSlotCount)
{
    public static GameState NewRoom(string roomCode, Schema schema) =>
        new(
            RoomCode: roomCode,
            Phase: RoomPhase.Lobby,
            HostId: Guid.Empty,
            Players: [],
            Schema: schema,
            NextJoinOrder: 0,
            Round: 0,
            Phrases: [],
            SubmittedThisRound: new HashSet<Guid>(),
            RevealPhraseIndex: 0,
            RevealSlotCount: 0);

    public Player? FindPlayer(Guid id) => Players.FirstOrDefault(p => p.Id == id);

    public int IndexOfPlayer(Guid id)
    {
        for (var i = 0; i < Players.Count; i++)
        {
            if (Players[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
