using FrasiSquisite.App.Services;

namespace FrasiSquisite.App.Tests;

/// <summary>
/// Sostituto in memoria di <see cref="IThemeService"/> per i test della
/// ViewModel: nessuna persistenza, solo lo stato e l'evento che
/// <see cref="GameSessionViewModel"/> osserva. Il comportamento vero del
/// servizio (persistenza, rilettura) è provato a parte in
/// <c>Services/ThemeServiceTests.cs</c> contro l'implementazione reale.
/// </summary>
public sealed class FakeThemeService : IThemeService
{
    public ThemeChoice Current { get; private set; } = ThemeChoice.SurrealistaPop;

    public event Action<ThemeChoice>? ThemeChanged;

    public List<ThemeChoice> Impostati { get; } = [];

    public void SetTheme(ThemeChoice theme)
    {
        Current = theme;
        Impostati.Add(theme);
        ThemeChanged?.Invoke(theme);
    }

    public void ApplyInitial() => ThemeChanged?.Invoke(Current);
}
