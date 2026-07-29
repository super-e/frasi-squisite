using FrasiSquisite.Domain.Randomness;

namespace FrasiSquisite.Server.Rooms;

public sealed class RoomCodeGenerator(IRandomSource random)
{
    public const int CodeLength = 4;

    /// <summary>
    /// Niente 0/O né 1/I/L: il codice si detta a voce o si legge da un altro
    /// telefono, e le ambiguità costano tentativi falliti.
    /// </summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string Next()
    {
        return string.Create(CodeLength, random, static (span, rnd) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Alphabet[rnd.Next(Alphabet.Length)];
            }
        });
    }
}
