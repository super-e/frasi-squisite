using System.Text.RegularExpressions;

namespace FrasiSquisite.Domain.Refinement;

/// <summary>
/// Decide, casella per casella, se fidarsi di quello che il modello ha
/// restituito. Un prompt e' una preghiera: la garanzia sta qui, in codice
/// puro e provabile, non nelle istruzioni che si mandano (spec §4.3).
/// </summary>
public static partial class RefinementGuard
{
    /// <summary>
    /// Tetto di sicurezza, non la validazione dell'input umano: quella e' a
    /// 60 caratteri e non si applica qui, perche' rifinire allunga sempre.
    /// Serve solo a impedire che il modello restituisca un paragrafo.
    /// </summary>
    public const int MaxCaratteri = 200;

    /// <param name="grezze">Le caselle come le hanno scritte i giocatori.</param>
    /// <param name="rifinite">Quelle tornate dal modello, o null se non e' tornato nulla.</param>
    /// <param name="template">Il template dello schema: dice cosa precede ogni segnaposto.</param>
    public static IReadOnlyList<string> Applica(
        IReadOnlyList<string> grezze,
        IReadOnlyList<string>? rifinite,
        string template)
    {
        ArgumentNullException.ThrowIfNull(grezze);
        ArgumentNullException.ThrowIfNull(template);

        // Un numero diverso di caselle non e' recuperabile a pezzi: non si sa
        // piu' quale corrisponde a quale, quindi si scarta tutto.
        if (rifinite is null || rifinite.Count != grezze.Count)
        {
            return grezze;
        }

        var precedenti = LetteraliPrecedenti(template, grezze.Count);
        var esito = new string[grezze.Count];

        for (var i = 0; i < grezze.Count; i++)
        {
            esito[i] = Accettabile(grezze[i], rifinite[i], precedenti[i]) ? rifinite[i] : grezze[i];
        }

        return esito;
    }

    private static bool Accettabile(string grezza, string rifinita, string precedente)
    {
        if (string.IsNullOrWhiteSpace(rifinita) || rifinita.Length > MaxCaratteri)
        {
            return false;
        }

        var r = Normalizza(rifinita);

        // Il modello non puo' riscrivere: le parole del giocatore devono
        // ricomparire dentro la casella rifinita.
        if (!r.Contains(Normalizza(grezza), StringComparison.Ordinal))
        {
            return false;
        }

        // E non puo' ripetere cio' che il template gli mette gia' davanti.
        return string.IsNullOrEmpty(precedente)
            || !r.StartsWith(Normalizza(precedente), StringComparison.Ordinal);
    }

    private static string Normalizza(string testo) =>
        SpaziMultipli().Replace(testo.Trim(), " ").ToLowerInvariant();

    /// <summary>
    /// Per ogni segnaposto, il testo fisso che lo precede nel template. Per
    /// "{6}», ed è andata a finire che {7}." il testo grezzo prima di 7 e'
    /// "», ed è andata a finire che", ma quella punteggiatura di contorno
    /// (virgolette, virgole) appartiene alla chiusura del segnaposto
    /// precedente: il modello non la ripeterebbe comunque, quindi il
    /// confronto parte dalla prima vera parola, "ed è andata a finire che".
    /// Cosi' ci si accorge che il modello lo sta ripetendo, perche' il
    /// confronto e' su come INIZIA la casella rifinita.
    /// </summary>
    private static string[] LetteraliPrecedenti(string template, int caselle)
    {
        var esito = new string[caselle];

        for (var i = 0; i < caselle; i++)
        {
            var posizione = template.IndexOf($"{{{i}}}", StringComparison.Ordinal);
            if (posizione < 0)
            {
                esito[i] = string.Empty;
                continue;
            }

            var prima = template[..posizione];
            var precedente = template.LastIndexOf('}', Math.Max(posizione - 1, 0));

            var grezzo = precedente >= 0 && precedente < posizione
                ? prima[(precedente + 1)..]
                : prima;

            esito[i] = PunteggiaturaIniziale().Replace(grezzo.Trim(), string.Empty);
        }

        return esito;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaziMultipli();

    /// <summary>
    /// Punteggiatura e spazi in testa a una stringa: quella che introduce un
    /// segnaposto (virgolette, virgole, spazi) e che il modello non ripete
    /// mai insieme al resto del testo letterale.
    /// </summary>
    [GeneratedRegex(@"^[^\p{L}\p{N}]+")]
    private static partial Regex PunteggiaturaIniziale();
}
