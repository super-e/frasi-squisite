using CommunityToolkit.Mvvm.ComponentModel;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Una riga della classifica finale. Testo, voti e autori arrivano già decisi
/// dal server e non cambiano; l'illustrazione sì, quindi la riga è diventata
/// osservabile — ma solo per le tre proprietà che cambiano davvero.
/// </summary>
public sealed partial class PhraseResultRowView(PhraseResultView risultato, bool isHost) : ObservableObject
{
    /// <summary>
    /// Serve per cercare la riga giusta quando arriva un
    /// <see cref="IllustrationReadyMessage"/> o <see cref="IllustrationFailedMessage"/>:
    /// la classifica è ordinata per voti, quindi la posizione nella lista non
    /// corrisponde all'indice della frase nel motore.
    /// </summary>
    public int PhraseIndex { get; } = risultato.PhraseIndex;

    public string Text { get; } = risultato.Text;

    public bool IsWinner { get; } = risultato.IsWinner;

    public string VotesLabel { get; } = risultato.Votes == 1 ? "1 voto" : $"{risultato.Votes} voti";

    public string AuthorsLabel { get; } = risultato.Authors.Count == 0
        ? string.Empty
        : $"Scritta da: {string.Join(" · ", risultato.Authors)}";

    /// <summary>
    /// Il pulsante esiste solo per chi ospita: il server rifiuterebbe comunque
    /// gli altri, ma mostrare un pulsante che dà errore è una bugia.
    /// </summary>
    public bool IsHost { get; } = isHost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRequest))]
    private bool _isWaiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRequest))]
    private string? _imageUrl;

    public bool CanRequest => IsHost && !IsWaiting && ImageUrl is null;
}
