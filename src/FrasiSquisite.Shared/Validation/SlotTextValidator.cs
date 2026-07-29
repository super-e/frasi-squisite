using System.Text.RegularExpressions;

namespace FrasiSquisite.Shared.Validation;

public readonly record struct SlotTextValidation(bool IsValid, string? Error, string Normalized)
{
    public static SlotTextValidation Ok(string normalized) => new(true, null, normalized);

    public static SlotTextValidation Fail(string error) => new(false, error, string.Empty);
}

/// <summary>
/// Validazione del testo di una casella. Vive in Shared perché il client la usa
/// per il feedback immediato e il server la riapplica non potendosi fidare del
/// client: unico codice, nessuna divergenza possibile.
/// </summary>
public static partial class SlotTextValidator
{
    public const int MaxLength = 60;

    public static SlotTextValidation Validate(string? testo)
    {
        if (string.IsNullOrWhiteSpace(testo))
        {
            return SlotTextValidation.Fail("Scrivi qualcosa.");
        }

        if (testo.Any(char.IsControl))
        {
            return SlotTextValidation.Fail("Niente a capo o caratteri strani.");
        }

        var normalizzato = SpaziMultipli().Replace(testo.Trim(), " ");

        if (normalizzato.Length > MaxLength)
        {
            return SlotTextValidation.Fail($"Massimo {MaxLength} caratteri.");
        }

        return SlotTextValidation.Ok(normalizzato);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaziMultipli();
}
