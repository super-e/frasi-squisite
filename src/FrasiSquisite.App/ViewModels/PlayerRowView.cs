using CommunityToolkit.Mvvm.ComponentModel;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Avvolge una <see cref="PlayerView"/> (contratto di rete) con lo stato di
/// presentazione "sto rinominando questo bot". <c>IsEditing</c> non deve mai
/// entrare in <see cref="PlayerView"/>, che attraversa la rete: è per questo
/// che serve un tipo a parte qui nella ViewModel (lotto-b-brief.md, punto 3).
/// </summary>
public sealed partial class PlayerRowView : ObservableObject
{
    public PlayerRowView(PlayerView player, bool viewerIsHost)
    {
        Player = player;
        ViewerIsHost = viewerIsHost;
    }

    public PlayerView Player { get; }

    public Guid Id => Player.Id;

    public string Nickname => Player.Nickname;

    public bool IsHost => Player.IsHost;

    public bool IsConnected => Player.IsConnected;

    public bool IsBot => Player.IsBot;

    /// <summary>
    /// Vero se chi guarda questa riga (il giocatore locale) è l'host della
    /// stanza. Non è <see cref="IsHost"/>, che dice se QUESTA riga è l'host:
    /// serve un valore separato per gate-are matita e ✕ come già fa
    /// <c>CanAddBot</c> nella ViewModel, altrimenti un non-host che tocca ✏
    /// riceverebbe solo un banner NOT_HOST invece di non vedere affatto i
    /// controlli. Fissato alla costruzione: la riga viene ricreata a ogni
    /// RoomStateMessage, quindi non serve renderlo osservabile.
    /// </summary>
    public bool ViewerIsHost { get; }

    [ObservableProperty]
    private bool _isEditing;

    /// <summary>
    /// Matita e ✕ della riga normale: visibili solo per un bot non in
    /// modifica, e solo per chi è host (lotto-b-brief.md). I giocatori umani
    /// non li mostrano mai, né li mostra un non-host per un bot altrui.
    /// </summary>
    public bool ShowBotControls => IsBot && !IsEditing && ViewerIsHost;

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(ShowBotControls));
}
