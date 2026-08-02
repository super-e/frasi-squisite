namespace FrasiSquisite.Domain.Model;

/// <summary>
/// Results non è una fase a sé: la classifica è ciò che si vede in
/// <see cref="Finished"/>, e una fase in più senza transizioni proprie
/// sarebbe stato morto.
/// </summary>
public enum RoomPhase
{
    Lobby,
    Writing,
    Reveal,
    Voting,
    Finished,
}
