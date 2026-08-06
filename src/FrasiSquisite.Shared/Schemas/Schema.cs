using System.Globalization;
using System.Text.RegularExpressions;

namespace FrasiSquisite.Shared.Schemas;

/// <summary>
/// Un pezzo del <see cref="Schema.Template"/>, nell'ordine di lettura: testo
/// fisso (<see cref="IsSlot"/> false) o casella (<see cref="IsSlot"/> true,
/// <see cref="SlotIndex"/> l'indice del segnaposto). Serve al reveal
/// (backlog #1) per intercalare il tessuto connettivo del template alle
/// caselle già scoperte, senza comporre l'intera frase in anticipo.
/// </summary>
public sealed record TemplateSegment
{
    public bool IsSlot { get; }
    public string Literal { get; }
    public int SlotIndex { get; }

    private TemplateSegment(bool isSlot, string literal, int slotIndex)
    {
        IsSlot = isSlot;
        Literal = literal;
        SlotIndex = slotIndex;
    }

    public static TemplateSegment OfLiteral(string text) => new(false, text, -1);

    public static TemplateSegment OfSlot(int slotIndex) => new(true, string.Empty, slotIndex);
}

public sealed record Schema(
    string Id,
    int Version,
    string Nome,
    IReadOnlyList<Casella> Caselle,
    string Template)
{
    public const string DefaultId = "storia";

    private static readonly Regex PosizioneSegnaposto = new(@"\{(\d+)\}", RegexOptions.Compiled);

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

    /// <summary>
    /// Il <see cref="Template"/> spezzato in ordine di lettura fra testo
    /// fisso e caselle (backlog #1): a differenza di <see cref="Compose"/>,
    /// che produce la frase intera in un colpo solo, questa scomposizione
    /// permette di mostrare il tessuto connettivo anche quando non tutte le
    /// caselle sono ancora state scoperte.
    /// </summary>
    public IReadOnlyList<TemplateSegment> Segments
    {
        get
        {
            var segmenti = new List<TemplateSegment>();
            var cursore = 0;

            foreach (Match m in PosizioneSegnaposto.Matches(Template))
            {
                if (m.Index > cursore)
                {
                    segmenti.Add(TemplateSegment.OfLiteral(Template[cursore..m.Index]));
                }

                segmenti.Add(TemplateSegment.OfSlot(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
                cursore = m.Index + m.Length;
            }

            if (cursore < Template.Length)
            {
                segmenti.Add(TemplateSegment.OfLiteral(Template[cursore..]));
            }

            return segmenti;
        }
    }
}
