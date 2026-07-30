namespace FrasiSquisite.App.Services;

/// <summary>
/// I due temi del design (lotto-a-brief.md): non seguono il chiaro/scuro di
/// sistema, li sceglie l'utente in Impostazioni.
/// </summary>
public enum ThemeChoice
{
    SurrealistaPop,
    NotteDiGioco,
}

/// <summary>
/// La ViewModel dipende da questa interfaccia e mai da <c>Preferences</c> o da
/// <c>Application.Current</c>: <c>GameSessionViewModel</c> è compilata anche
/// nel progetto di test, che ha come target <c>net10.0</c> puro e non vede MAUI.
/// </summary>
public interface IThemeService
{
    ThemeChoice Current { get; }

    /// <summary>
    /// Si solleva ogni volta che il tema cambia, incluso durante
    /// l'inizializzazione (rilettura del valore persistito all'avvio): chi
    /// applica davvero il ResourceDictionary (che vive nel progetto App, non
    /// qui) si aggancia a questo evento invece di duplicare la logica di
    /// lettura delle preferenze.
    /// </summary>
    event Action<ThemeChoice>? ThemeChanged;

    void SetTheme(ThemeChoice theme);

    /// <summary>
    /// Da chiamare una volta all'avvio (progetto App), dopo essersi iscritti a
    /// <see cref="ThemeChanged"/>: risolleva l'evento con <see cref="Current"/>
    /// così che l'applicazione del ResourceDictionary iniziale passi dallo
    /// stesso percorso di un cambio a runtime, invece di duplicarlo.
    /// </summary>
    void ApplyInitial();
}

/// <summary>
/// Astrae la persistenza del tema scelto. In produzione lo implementa un
/// wrapper su <c>Preferences.Default</c> (progetto App, MAUI); nei test lo
/// sostituisce un dizionario in memoria: così <see cref="ThemeService"/> resta
/// provabile senza MAUI pur avendo un comportamento di persistenza reale.
/// </summary>
public interface IThemeStore
{
    ThemeChoice? Load();

    void Save(ThemeChoice theme);
}
