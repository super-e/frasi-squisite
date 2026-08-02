namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Una frase su cui si può votare. <see cref="Index"/> è la posizione nella
/// lista arrivata dal server ed è ciò che si rimanda indietro: la riga non
/// porta autori, il voto è cieco (spec §3).
/// </summary>
public sealed class VoteOptionView(int index, string text)
{
    public int Index { get; } = index;

    public string Text { get; } = text;
}
