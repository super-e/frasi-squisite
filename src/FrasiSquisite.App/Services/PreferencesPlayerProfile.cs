namespace FrasiSquisite.App.Services;

/// <summary>
/// Persistenza del nickname in <c>Preferences</c>: non è un segreto (a
/// differenza dell'id giocatore in <c>SecureStorage</c>, vedi
/// <c>PlayerIdentity</c> in <c>MauiProgram</c>), quindi non serve cifratura,
/// ed è sincrona - niente <c>Task.Run</c> come invece serve a
/// <c>PlayerIdentity</c> per evitare un deadlock sul <c>SynchronizationContext</c>.
/// Stesso schema di <see cref="PreferencesThemeStore"/>: unico punto del
/// progetto App che tocca MAUI per il nickname, il resto (la ViewModel) resta
/// provabile senza.
/// </summary>
public sealed class PreferencesPlayerProfile : IPlayerProfile
{
    private const string Key = "nickname-salvato";

    public string Nickname => Preferences.Default.Get(Key, string.Empty);

    public void SaveNickname(string nickname) => Preferences.Default.Set(Key, nickname);
}
