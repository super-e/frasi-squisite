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
    public PlayerRowView(PlayerView player)
    {
        Player = player;
    }

    public PlayerView Player { get; }

    public Guid Id => Player.Id;

    public string Nickname => Player.Nickname;

    public bool IsHost => Player.IsHost;

    public bool IsConnected => Player.IsConnected;

    public bool IsBot => Player.IsBot;

    [ObservableProperty]
    private bool _isEditing;

    /// <summary>
    /// Matita e ✕ della riga normale: visibili solo per un bot non in
    /// modifica. I giocatori umani non li mostrano mai (lotto-b-brief.md).
    /// </summary>
    public bool ShowBotControls => IsBot && !IsEditing;

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(ShowBotControls));
}
