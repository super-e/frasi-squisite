using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Model;

public sealed record GameState(
    string RoomCode,
    RoomPhase Phase,
    Guid HostId,
    IReadOnlyList<Player> Players,
    Schema Schema,
    IReadOnlyList<SchemaView> AvailableSchemas,
    long NextJoinOrder,
    int Round,
    IReadOnlyList<Phrase> Phrases,
    IReadOnlySet<Guid> SubmittedThisRound,
    int RevealPhraseIndex,
    int RevealSlotCount,
    IReadOnlyDictionary<Guid, int> Votes,
    IReadOnlySet<int> IllustrationsRequested)
{
    /// <summary>
    /// <paramref name="availableSchemas"/> è annullabile solo per lasciare
    /// invariate le decine di test esistenti che chiamano
    /// <c>NewRoom("ABCD", TestSchemas.WithSlots(k))</c> senza catalogo
    /// (lotto-c-brief.md): il default è una lista vuota, mai null.
    /// </summary>
    public static GameState NewRoom(
        string roomCode,
        Schema schema,
        IReadOnlyList<SchemaView>? availableSchemas = null) =>
        new(
            RoomCode: roomCode,
            Phase: RoomPhase.Lobby,
            HostId: Guid.Empty,
            Players: [],
            Schema: schema,
            AvailableSchemas: availableSchemas ?? [],
            NextJoinOrder: 0,
            Round: 0,
            Phrases: [],
            SubmittedThisRound: new HashSet<Guid>(),
            RevealPhraseIndex: 0,
            RevealSlotCount: 0,
            Votes: new Dictionary<Guid, int>(),
            // Solo gli indici: al motore serve sapere se una frase è già stata
            // chiesta, non dove sia finita l'immagine. Gli indirizzi li manda
            // e non li rilegge, e tenerli avvicinerebbe il motore ai byte, che
            // la spec §5 vuole fuori.
            IllustrationsRequested: new HashSet<int>());

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
