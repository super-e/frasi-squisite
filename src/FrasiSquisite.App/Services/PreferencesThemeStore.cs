namespace FrasiSquisite.App.Services;

/// <summary>
/// Persistenza del tema in <c>Preferences</c>: non è un segreto (a differenza
/// dell'id giocatore in <c>SecureStorage</c>, vedi <see cref="PlayerIdentity"/>),
/// quindi non serve cifratura. Unico punto del progetto App che tocca MAUI per
/// il tema: il resto (<see cref="ThemeService"/>) è provabile senza.
/// </summary>
public sealed class PreferencesThemeStore : IThemeStore
{
    private const string Key = "tema-scelto";

    public ThemeChoice? Load()
    {
        var salvato = Preferences.Default.Get(Key, string.Empty);
        return Enum.TryParse<ThemeChoice>(salvato, out var valore) ? valore : null;
    }

    public void Save(ThemeChoice theme) => Preferences.Default.Set(Key, theme.ToString());
}
