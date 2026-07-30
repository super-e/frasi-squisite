using CommunityToolkit.Mvvm.ComponentModel;
using FrasiSquisite.Shared.Protocol;

namespace FrasiSquisite.App.ViewModels;

/// <summary>
/// Avvolge una <see cref="SchemaView"/> (contratto di rete) con lo stato di
/// presentazione "è quello selezionato in questo momento". Stesso motivo di
/// <see cref="PlayerRowView"/> per <c>IsEditing</c>: <c>IsSelected</c> non
/// deve mai entrare in <see cref="SchemaView"/>, che attraversa la rete
/// (lotto-c-brief.md).
/// </summary>
public sealed partial class SchemaOptionView : ObservableObject
{
    public SchemaOptionView(SchemaView schema, bool isSelected)
    {
        Schema = schema;
        _isSelected = isSelected;
    }

    public SchemaView Schema { get; }

    public string Id => Schema.Id;

    public string Nome => Schema.Nome;

    public int SlotCount => Schema.SlotCount;

    [ObservableProperty]
    private bool _isSelected;
}
