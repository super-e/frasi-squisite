using System.Collections.Frozen;
using FrasiSquisite.Domain.Randomness;

namespace FrasiSquisite.Domain.Filling;

/// <summary>
/// Dizionario compilato nel binario. Deve funzionare senza rete e senza AI:
/// è la garanzia che una partita non si blocchi mai (spec §8.5).
/// </summary>
public sealed class StaticWordPool : IWordPool
{
    // La chiave è il ruolo, non lo schema: ruoli con lo stesso nome in schemi
    // diversi condividono le parole, di proposito. "Verbo" vale sia per il
    // surrealista ("Il notaio divora il tramonto") sia per il titolo di
    // giornale ("Il sindaco divora una rotonda in pigiama") — la grammatica
    // slitta un po', l'effetto comico regge, e chi scrive uno schema nuovo
    // riusa i ruoli esistenti senza dover ripetere le parole.
    private static readonly FrozenDictionary<string, string[]> PerRuolo =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- surrealista-classico, titolo-di-giornale ----
            ["Soggetto"] = ["Il notaio", "La pantofola", "Un tram", "Il vescovo", "La zuppa", "Un ombrello"],
            ["Aggettivo"] = ["distratto", "elettrico", "sbilenco", "solenne", "tiepido", "invisibile"],
            ["Verbo"] = ["divora", "sussurra", "scavalca", "dimentica", "corteggia", "rimpiange"],
            ["Complemento"] = ["il tramonto", "una scala", "il silenzio", "tre valigie", "la domenica", "un lampione"],
            ["Avverbio"] = ["lentamente", "di nascosto", "per sbaglio", "controvoglia", "all'improvviso"],
            ["Circostanza"] = [
                "in pigiama", "durante il telegiornale", "senza avvisare nessuno",
                "sotto la pioggia", "per la terza volta", "a mezzanotte in punto"],

            // ---- proverbio ----
            ["Premessa"] = [
                "Chi va piano", "Chi dorme in piedi", "Chi semina cipolle",
                "Chi non guarda in faccia nessuno", "Chi arriva ultimo", "Chi si accontenta"],
            ["Conseguenza"] = [
                "mangia le pere", "trova il portone chiuso", "diventa sindaco",
                "perde l'ombrello", "si sposa due volte", "non paga il conto"],
            ["Rincaro"] = [
                "e non ringrazia", "e poi si lamenta", "ma non lo ammette",
                "e dorme benissimo", "e nessuno dice niente", "e intanto piove"],

            // ---- oroscopo ----
            ["Segno"] = [
                "Vergine", "Capricorno", "Acquario",
                "Bilancia ascendente lavatrice", "Scorpione", "Toro"],
            ["Previsione"] = [
                "un incontro inatteso", "una bolletta imprevista", "un lieve malinteso",
                "una gioia breve", "un pacco che non aspettavi", "una domenica lunghissima"],
            ["Luogo"] = [
                "in ascensore", "alle poste", "dietro il divano",
                "in tangenziale", "nel reparto surgelati", "sul pianerottolo"],
            ["Consiglio"] = [
                "non fidarti dei mercoledì", "evita le scale mobili", "richiama tua zia",
                "compra meno pane", "non firmare niente", "cambia pettinatura"],
            ["Numero"] = [
                "il 47", "il 13, ma sottovoce", "il numero che hai sognato",
                "l'8, come sempre", "il 90", "nessuno, quest'anno"],

            // ---- ricetta ----
            ["Ingrediente"] = [
                "Tre cipolle", "Mezzo chilo di rimpianti", "Due uova, forse tre",
                "Un litro di brodo tiepido", "Una manciata di viti", "Sette foglie d'alloro"],
            ["Azione"] = [
                "vanno frullate", "si lasciano riposare", "vanno bollite a lungo",
                "si tritano male", "vanno dimenticate in frigo", "si schiacciano con le mani"],
            ["Modo"] = [
                "con rabbia", "senza convinzione", "in gran segreto",
                "cantando", "molto lentamente", "come faceva la nonna"],
            ["Aggiunta"] = [
                "del bicarbonato", "una spolverata di cemento", "il succo di un limone stanco",
                "due cucchiai di panna", "quello che avanza", "un pizzico di sale grosso"],
            ["Servizio"] = [
                "tiepido, ai nemici", "freddo, in silenzio", "a tavola, senza spiegazioni",
                "in piedi, di corsa", "il giorno dopo", "solo alle visite"],

            // ---- storia (schema di default) ----
            ["Chi?"] = [
                "Un pinguino in doppiopetto", "Il ragioniere del quinto piano",
                "Una lavatrice con ambizioni", "Il campione di briscola",
                "Una statua distratta", "Il cugino che nessuno invita"],
            ["Con chi?"] = [
                "insieme al suo commercialista", "con un cane che non è suo",
                "in compagnia di due gemelli", "contro tutta la palazzina",
                "insieme alla ex moglie", "con il portiere di notte"],
            ["Dove?"] = [
                "nella sala d'attesa di un dentista", "in una rotonda mai finita",
                "dentro un armadio a tre ante", "all'ufficio oggetti smarriti",
                "su una funivia ferma", "nel retro di una pescheria"],
            ["Cosa fa?"] = [
                "monta una libreria svedese", "vende scarpe a nessuno", "impara il flauto",
                "riordina l'alfabeto", "traduce il menù", "spegne tutte le luci"],
            ["Perché?"] = [
                "perché gliel'ha detto l'oroscopo", "perché aveva perso una scommessa",
                "per far dispetto al fratello", "perché era già pagato",
                "per via di un sogno", "perché nessuno gliel'ha impedito"],
            ["Cosa dicono?"] = [
                "Non è colpa mia, io ho solo firmato", "Lo rifarei, ma più piano",
                "Avevo capito un'altra cosa", "Chiamate mia sorella",
                "Non toccate niente", "Era scritto sul foglietto"],
            ["Cosa dice la gente?"] = [
                "Si sapeva che finiva così", "Non era mica il primo",
                "Io l'avevo detto a mia moglie", "Adesso chi paga?",
                "Sono cose che capitano", "Bisognerebbe fare qualcosa"],
            ["Com'è andata a finire?"] = [
                "sono finiti tutti al telegiornale", "non se n'è accorto nessuno",
                "hanno chiuso la strada per due giorni", "adesso c'è un cartello",
                "è diventata una tradizione", "hanno dato la colpa al vento"],
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] Generico =
        ["qualcosa", "un tale", "altrove", "comunque", "una cosa", "chissà"];

    /// <summary>
    /// I ruoli per cui esistono parole dedicate. Serve a poter affermare che
    /// nessuno schema del catalogo ricade sul generico: <see cref="Take"/> da
    /// solo non lo direbbe, perché il fallback restituisce comunque una parola
    /// valida e la partita prosegue — male, ma prosegue.
    /// </summary>
    public static IReadOnlyCollection<string> RuoliCoperti => PerRuolo.Keys;

    public string Take(string ruolo, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var parole = PerRuolo.TryGetValue(ruolo, out var perRuolo) ? perRuolo : Generico;

        return parole[random.Next(parole.Length)];
    }
}
