using FrasiSquisite.App.Services;

namespace FrasiSquisite.App.Tests.Services;

/// <summary>
/// Sostituto in memoria di <see cref="IThemeStore"/>: in produzione è
/// <c>PreferencesThemeStore</c> (Preferences, MAUI), qui un campo. Serve a
/// provare <see cref="ThemeService"/> - persistenza e rilettura - senza MAUI.
/// </summary>
public sealed class FakeThemeStore : IThemeStore
{
    private ThemeChoice? _salvato;

    public ThemeChoice? Load() => _salvato;

    public void Save(ThemeChoice theme) => _salvato = theme;
}
