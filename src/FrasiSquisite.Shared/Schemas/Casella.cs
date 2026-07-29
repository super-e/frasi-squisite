namespace FrasiSquisite.Shared.Schemas;

/// <summary>
/// Una casella dello schema grammaticale. Il <paramref name="Ruolo"/> è testo
/// libero e non un enum: aggiungere ruoli nuovi deve restare una modifica al
/// JSON, non al codice (spec §6).
/// </summary>
public sealed record Casella(string Ruolo, string Prompt, string Esempio);
