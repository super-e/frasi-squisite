namespace FrasiSquisite.App.Services;

/// <summary>
/// Astrae la persistenza del codice della stanza in cui si è in partita,
/// stesso schema di <see cref="IPlayerProfile"/>: la ViewModel dipende da
/// questa interfaccia e mai da <c>Preferences</c> direttamente, perché
/// <c>GameSessionViewModel</c> è compilata anche nel progetto di test.
/// </summary>
public interface IRoomSession
{
    /// <summary>Il codice stanza salvato, o <see cref="string.Empty"/> se non c'è una partita in sospeso.</summary>
    string RoomCode { get; }

    /// <summary>Salva il codice stanza: aggiornato a ogni RoomStateMessage ricevuto.</summary>
    void Save(string roomCode);

    /// <summary>
    /// Cancella il codice salvato: da chiamare solo quando il server rifiuta
    /// esplicitamente un tentativo di rientro (design rientro §5.1) — non
    /// quando si torna alla lobby o una partita finisce, perché la stanza
    /// resta comunque valida in entrambi i casi.
    /// </summary>
    void Clear();
}
