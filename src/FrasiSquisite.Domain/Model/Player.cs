namespace FrasiSquisite.Domain.Model;

/// <summary>
/// <paramref name="JoinOrder"/> è un contatore monotono, non un timestamp: il
/// motore non può leggere l'orologio, e per stabilire "chi è presente da più
/// tempo" (successione dell'host) un ordinale basta e avanza.
/// </summary>
public sealed record Player(Guid Id, string Nickname, bool IsBot, long JoinOrder, bool IsConnected);
