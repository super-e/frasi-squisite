namespace FrasiSquisite.App.Services;

/// <summary>
/// Persistenza del codice stanza in <c>Preferences</c>: non è un segreto (a
/// differenza dell'id giocatore in <c>SecureStorage</c>, vedi
/// <c>PlayerIdentity</c> in <c>MauiProgram</c>), stesso schema di
/// <see cref="PreferencesPlayerProfile"/>.
/// </summary>
public sealed class PreferencesRoomSession : IRoomSession
{
    private const string Key = "stanza-in-sospeso";

    public string RoomCode => Preferences.Default.Get(Key, string.Empty);

    public void Save(string roomCode) => Preferences.Default.Set(Key, roomCode);

    public void Clear() => Preferences.Default.Remove(Key);
}
