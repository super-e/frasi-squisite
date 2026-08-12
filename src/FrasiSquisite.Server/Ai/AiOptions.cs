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
    /// Base del tempo concesso alla rifinitura, prima di procedere con le
    /// caselle grezze. Non e' un'ottimizzazione: e' cio' che impedisce a una
    /// partita di restare appesa (spec §4.4). Cresce con
    /// <see cref="TimeoutSecondiPerFraseAggiuntiva"/> fino al tetto di
    /// <see cref="TimeoutMassimoSecondi"/> (design 2026-08-12 "migliora la
    /// rifinitura", §3.1): un valore fisso a 10s scadeva sistematicamente,
    /// anche con poche frasi.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// La rifinitura e' un'unica chiamata per tutte le frasi della partita:
    /// piu' giocatori, piu' caselle da rifinire nello stesso giro. Ogni
    /// frase oltre la prima allunga il tempo concesso di questi secondi.
    /// </summary>
    public int TimeoutSecondiPerFraseAggiuntiva { get; set; } = 3;

    /// <summary>
    /// Tetto oltre il quale il tempo concesso non cresce piu', anche con
    /// molte frasi: una partita numerosa non deve far aspettare tutti quasi
    /// mezzo minuto in piu' di quanto gia' previsto.
    /// </summary>
    public int TimeoutMassimoSecondi { get; set; } = 30;

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
