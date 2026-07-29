namespace FrasiSquisite.Domain.Model;

/// <summary>
/// Fasi della Fase 1. Voting e Results arrivano nella fase implementativa
/// successiva: non anticiparli qui (spec §13).
/// </summary>
public enum RoomPhase
{
    Lobby,
    Writing,
    Reveal,
    Finished,
}
