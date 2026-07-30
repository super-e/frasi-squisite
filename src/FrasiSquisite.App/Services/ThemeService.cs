namespace FrasiSquisite.App.Services;

/// <summary>
/// Logica del tema, senza MAUI: legge la scelta persistita (tramite
/// <see cref="IThemeStore"/>) alla costruzione, la mantiene in
/// <see cref="Current"/> e la ripersiste a ogni cambio. Chi applica davvero il
/// cambio alla UI (scambio di ResourceDictionary) vive nel progetto App e si
/// aggancia a <see cref="ThemeChanged"/>: questa classe non sa che la UI
/// esiste, esattamente come <see cref="FrasiSquisite.App.ViewModels.GameSessionViewModel"/>
/// non sa che SignalR esiste.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly IThemeStore _store;

    public ThemeService(IThemeStore store)
    {
        _store = store;
        Current = _store.Load() ?? ThemeChoice.SurrealistaPop;
    }

    public ThemeChoice Current { get; private set; }

    public event Action<ThemeChoice>? ThemeChanged;

    /// <summary>
    /// Da chiamare una volta all'avvio, dopo che chi ascolta
    /// <see cref="ThemeChanged"/> si è iscritto: applica il tema già letto dal
    /// costruttore (di norma quello persistito, "a" se è il primo avvio) così
    /// che l'applicazione dello sfondo iniziale passi dallo stesso percorso di
    /// un cambio a runtime, invece di duplicarlo.
    /// </summary>
    public void ApplyInitial() => ThemeChanged?.Invoke(Current);

    public void SetTheme(ThemeChoice theme)
    {
        Current = theme;
        _store.Save(theme);
        ThemeChanged?.Invoke(theme);
    }
}
