namespace FrasiSquisite.Server.Ai;

/// <summary>
/// Separata da <see cref="IAiTextProvider"/> perché l'endpoint è un altro
/// (/v1/images/generations) e la compatibilità OpenAI fra fornitori è meno
/// garantita sulle immagini che sul testo (spec §7).
///
/// Torna i byte e non un indirizzo: quello che restituisce ppq.ai è firmato e
/// scade, e passarlo ai client significherebbe mostrargli un riquadro rotto
/// poco dopo. Chi implementa decide come procurarseli.
///
/// Non lancia mai: qualunque guasto è <c>null</c>.
/// </summary>
public interface IAiImageProvider
{
    Task<byte[]?> GeneraAsync(string promptInglese, CancellationToken ct);
}
