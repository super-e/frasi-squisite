namespace FrasiSquisite.Domain.Model;

/// <summary>Una casella a <c>null</c> non è ancora stata scritta.</summary>
public sealed record Phrase(int Index, IReadOnlyList<Slot?> Slots)
{
    public bool IsComplete => Slots.All(s => s is not null);

    public static Phrase Empty(int index, int slotCount) =>
        new(index, new Slot?[slotCount]);

    public Phrase With(int slotIndex, Slot slot)
    {
        var caselle = Slots.ToArray();
        caselle[slotIndex] = slot;
        return this with { Slots = caselle };
    }
}
