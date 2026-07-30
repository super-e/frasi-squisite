using System.Text.RegularExpressions;

namespace FrasiSquisite.Shared.Validation;

public readonly record struct NicknameValidation(bool IsValid, string? Error, string Normalized)
{
    public static NicknameValidation Ok(string normalized) => new(true, null, normalized);

    public static NicknameValidation Fail(string error) => new(false, error, string.Empty);
}

/// <summary>
/// Validazione del nickname: sia quello scelto entrando in una stanza, sia
/// quello dato a un bot con <c>BotRenamed</c>. Vive in Shared per lo stesso
/// motivo di <see cref="SlotTextValidator"/>: client e server non possono
/// divergere se usano lo stesso codice (lotto-b-brief.md).
/// </summary>
public static partial class NicknameValidator
{
    public const int MaxLength = 20;

    public static NicknameValidation Validate(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return NicknameValidation.Fail("Scrivi un nome.");
        }

        if (nickname.Any(char.IsControl))
        {
            return NicknameValidation.Fail("Niente a capo o caratteri strani.");
        }

        var normalizzato = SpaziMultipli().Replace(nickname.Trim(), " ");

        if (normalizzato.Length > MaxLength)
        {
            return NicknameValidation.Fail($"Massimo {MaxLength} caratteri.");
        }

        return NicknameValidation.Ok(normalizzato);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaziMultipli();
}
