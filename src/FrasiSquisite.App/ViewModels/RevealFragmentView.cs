namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Un pezzo della frase nella schermata di reveal, 1:1 col
/// <c>RevealFragment</c> mandato dal server: testo fisso del template
/// (nessun riquadro, sempre visibile) o una casella (riquadro pieno se già
/// scoperta, tratteggiato con "···" se non ancora). Le tre proprietà
/// computate sono quelle che la UI usa per scegliere il ramo di rendering,
/// senza binding multipli nel markup (backlog #1).
/// </summary>
public sealed record RevealFragmentView(bool IsSlot, string Text, bool IsRevealed)
{
    public bool IsLiteral => !IsSlot;

    public bool ShowAsRevealedSlot => IsSlot && IsRevealed;

    public bool ShowAsCoveredSlot => IsSlot && !IsRevealed;
}
