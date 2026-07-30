namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Una casella nella schermata di reveal: o mostra il testo scoperto, o "···"
/// finché non tocca a lei (<see cref="IsRevealed"/> false). La lista ha sempre
/// lunghezza pari a <c>SlotCount</c> dello schema, non solo alle caselle già
/// arrivate dal server: è così che la UI sa quante ne mancano.
/// </summary>
public sealed record RevealSlotView(string Text, bool IsRevealed);
