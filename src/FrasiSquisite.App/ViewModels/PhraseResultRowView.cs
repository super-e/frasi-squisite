using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Una riga della classifica finale. Solo proiezione: l'ordine e il verdetto
/// arrivano già decisi dal server (spec §7), qui si formattano soltanto le
/// etichette che la XAML non saprebbe comporre da sola.
/// </summary>
public sealed class PhraseResultRowView(PhraseResultView risultato)
{
    public string Text { get; } = risultato.Text;

    public bool IsWinner { get; } = risultato.IsWinner;

    public string VotesLabel { get; } = risultato.Votes == 1 ? "1 voto" : $"{risultato.Votes} voti";

    public string AuthorsLabel { get; } = risultato.Authors.Count == 0
        ? string.Empty
        : $"Scritta da: {string.Join(" · ", risultato.Authors)}";
}
