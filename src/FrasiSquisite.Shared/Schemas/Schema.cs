using System.Globalization;

namespace FrasiSquisite.Shared.Schemas;

public sealed record Schema(
    string Id,
    int Version,
    string Nome,
    IReadOnlyList<Casella> Caselle,
    string Template)
{
    public const string DefaultId = "surrealista-classico";

    public int SlotCount => Caselle.Count;

    /// <summary>
    /// Compone la frase finale. Il template usa segnaposto numerati, così una
    /// casella può in futuro comparire più volte o in ordine diverso da quello
    /// di scrittura senza cambiare il formato dei dati (spec §6).
    /// </summary>
    public string Compose(IReadOnlyList<string> valori)
    {
        ArgumentNullException.ThrowIfNull(valori);

        if (valori.Count != SlotCount)
        {
            throw new ArgumentException(
                $"Lo schema '{Id}' ha {SlotCount} caselle, ricevuti {valori.Count} valori.",
                nameof(valori));
        }

        var argomenti = new object[valori.Count];
        for (var i = 0; i < valori.Count; i++)
        {
            argomenti[i] = valori[i];
        }

        return string.Format(CultureInfo.InvariantCulture, Template, argomenti);
    }
}
