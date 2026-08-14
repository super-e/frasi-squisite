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

    /// <summary>
    /// Base del tetto di token concessi alla risposta della rifinitura,
    /// prima di aggiungere il contributo delle caselle totali (vedi
    /// <see cref="RifinituraMaxTokensPerCasella"/>). Un tetto fisso a 2000
    /// (il valore precedente, prima che questo campo esistesse) troncava la
    /// risposta con partite numerose (backlog.md §5): 9 giocatori x 8
    /// caselle di uno schema = 72 caselle in un'unica risposta batch.
    /// </summary>
    public int RifinituraMaxTokensBase { get; set; } = 500;

    /// <summary>
    /// Quanti token in più concedere per ogni casella totale (frasi x
    /// caselle per frase) da rifinire nella stessa chiamata batch.
    /// </summary>
    public int RifinituraMaxTokensPerCasella { get; set; } = 120;

    public string ImageModel { get; set; } = "nano-banana-2";

    /// <summary>
    /// 1K basta per un telefono e costa circa nove centesimi; 2K e 4K costano
    /// di più senza che si veda la differenza su uno schermo da sei pollici.
    /// </summary>
    public string ImageSize { get; set; } = "1K";

    /// <summary>
    /// Generare un'immagine richiede molto più tempo che correggere un testo, e
    /// il limite base della rifinitura (oggi 15s, fino a
    /// TimeoutMassimoSecondi con più frasi) la farebbe fallire sempre.
    /// Qui non c'è una partita che aspetta: l'host ha premuto un pulsante e sta
    /// guardando una rotellina, quindi si può essere pazienti.
    /// </summary>
    public int ImageTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// L'unico interruttore: senza chiave l'AI e' spenta e il gioco resta
    /// interamente giocabile (spec §7).
    /// </summary>
    public bool Abilitato => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// Tetto alle illustrazioni per singola partita conclusa (si azzera a
    /// ogni nuova partita, non "per serata": vedi la nota di scoping nel
    /// piano che ha introdotto questo campo). Default int.MaxValue: nessun
    /// tetto, comportamento identico a prima che questo campo esistesse.
    /// Ogni illustrazione costa circa nove centesimi (spec AI); un
    /// operatore che vuole limitare il costo lo configura esplicitamente
    /// (backlog.md §4, rilievo 7).
    /// </summary>
    public int MassimoIllustrazioniPerStanza { get; set; } = int.MaxValue;
}
