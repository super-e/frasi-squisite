using System.Collections.Frozen;
using FrasiSquisite.Domain.Randomness;

namespace FrasiSquisite.Domain.Filling;

/// <summary>
/// Dizionario compilato nel binario. Deve funzionare senza rete e senza AI:
/// è la garanzia che una partita non si blocchi mai (spec §8.5).
/// </summary>
public sealed class StaticWordPool : IWordPool
{
    private static readonly FrozenDictionary<string, string[]> PerRuolo =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Soggetto"] = ["Il notaio", "La pantofola", "Un tram", "Il vescovo", "La zuppa", "Un ombrello"],
            ["Aggettivo"] = ["distratto", "elettrico", "sbilenco", "solenne", "tiepido", "invisibile"],
            ["Verbo"] = ["divora", "sussurra", "scavalca", "dimentica", "corteggia", "rimpiange"],
            ["Complemento"] = ["il tramonto", "una scala", "il silenzio", "tre valigie", "la domenica", "un lampione"],
            ["Avverbio"] = ["lentamente", "di nascosto", "per sbaglio", "controvoglia", "all'improvviso"],
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] Generico =
        ["qualcosa", "un tale", "altrove", "comunque", "una cosa", "chissà"];

    public string Take(string ruolo, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var parole = PerRuolo.TryGetValue(ruolo, out var perRuolo) ? perRuolo : Generico;

        return parole[random.Next(parole.Length)];
    }
}
