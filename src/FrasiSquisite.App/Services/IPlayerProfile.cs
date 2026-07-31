namespace FrasiSquisite.App.Services;

/// <summary>
/// Astrae la persistenza del nickname scelto, con lo stesso schema di
/// <see cref="IThemeService"/>: la ViewModel dipende da questa interfaccia e
/// mai da <c>Preferences</c>, perché <c>GameSessionViewModel</c> è compilata
/// anche nel progetto di test, che ha come target <c>net10.0</c> puro e non
/// vede MAUI. In produzione la implementa un wrapper su
/// <c>Preferences.Default</c> (progetto App, MAUI, non
/// <c>SecureStorage</c>: un nickname non è un segreto, e <c>SecureStorage</c>
/// è asincrono - <c>PlayerIdentity</c> in <c>MauiProgram</c> deve già
/// avvolgerlo in un <c>Task.Run</c> per non rischiare un deadlock sul
/// contesto di sincronizzazione, e un secondo punto con lo stesso problema
/// non serve); nei test la sostituisce un campo in memoria.
/// </summary>
public interface IPlayerProfile
{
    /// <summary>
    /// Il nickname salvato, o <see cref="string.Empty"/> se non ce n'è
    /// ancora uno (primo avvio).
    /// </summary>
    string Nickname { get; }

    /// <summary>
    /// Salva il nickname. Da chiamare solo con un valore già validato e
    /// normalizzato da <c>NicknameValidator</c> (lotto-e-brief.md), e solo
    /// quando è stato effettivamente usato con successo - alla creazione o
    /// all'ingresso in una stanza - non a ogni tasto premuto, altrimenti si
    /// salverebbe anche il testo scritto a metà.
    /// </summary>
    void SaveNickname(string nickname);
}
