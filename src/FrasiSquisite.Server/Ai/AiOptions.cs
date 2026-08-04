namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Indirizzo, modello e chiave vengono dalla configurazione e non dal codice:
/// ppq.ai e OpenRouter espongono entrambi /chat/completions in formato
/// OpenAI, quindi cambiare fornitore e' una variabile d'ambiente e non una
/// modifica al codice (spec §7).
/// </summary>
public sealed class AiOptions
{
    public const string Sezione = "Ai";

    public string BaseUrl { get; set; } = "https://api.ppq.ai";

    /// <summary>
    /// Mai in appsettings.json, che finisce in git: arriva come variabile
    /// d'ambiente (Ai__ApiKey) dal file .env del container.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string TextModel { get; set; } = "glm-5.2";

    /// <summary>
    /// Oltre questo, si prosegue con le caselle grezze. Non e' un'ottimizzazione:
    /// e' cio' che impedisce a una partita di restare appesa (spec §4.4).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    public string ImageModel { get; set; } = "nano-banana-2";

    /// <summary>
    /// 1K basta per un telefono e costa circa nove centesimi; 2K e 4K costano
    /// di più senza che si veda la differenza su uno schermo da sei pollici.
    /// </summary>
    public string ImageSize { get; set; } = "1K";

    /// <summary>
    /// Generare un'immagine richiede molto più tempo che correggere un testo, e
    /// il limite della rifinitura (dieci secondi) la farebbe fallire sempre.
    /// Qui non c'è una partita che aspetta: l'host ha premuto un pulsante e sta
    /// guardando una rotellina, quindi si può essere pazienti.
    /// </summary>
    public int ImageTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// L'unico interruttore: senza chiave l'AI e' spenta e il gioco resta
    /// interamente giocabile (spec §7).
    /// </summary>
    public bool Abilitato => !string.IsNullOrWhiteSpace(ApiKey);
}
