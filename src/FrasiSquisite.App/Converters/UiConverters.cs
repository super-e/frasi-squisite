using System.Globalization;
using FrasiSquisite.App.Services;
using FrasiSquisite.App.ViewModels;

namespace FrasiSquisite.App.Converters;

public sealed class IsScreenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ScreenState screen
        && parameter is string atteso
        && string.Equals(screen.ToString(), atteso, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Confronta un <see cref="ThemeChoice"/> bindato col nome membro passato come
/// parametro: stessa idea di <see cref="IsScreenConverter"/> ma per il tema,
/// usata dalle due card di Impostazioni per sapere quale evidenziare.
/// </summary>
public sealed class IsThemeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ThemeChoice tema
        && parameter is string atteso
        && string.Equals(tema.ToString(), atteso, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Nega un bool: usato per l'IsVisible complementare a IsJoiningByCode.</summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}

/// <summary>
/// Maiuscolo in entrambe le direzioni: il CodeEntry della Home vuole
/// "maiuscolo automatico" mentre si scrive, non solo al momento dell'invio.
/// Entry non ha un TextTransform nativo (a differenza di Label), quindi il
/// binding stesso normalizza il valore.
/// </summary>
public sealed class UppercaseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string)?.ToUpperInvariant() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string)?.ToUpperInvariant() ?? string.Empty;
}

/// <summary>Prima lettera del nickname, maiuscola: l'iniziale nell'Avatar.</summary>
public sealed class InitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } testo
            ? testo[..1].ToUpperInvariant()
            : "?";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
