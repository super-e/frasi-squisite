namespace FrasiSquisite.Shared.Schemas;

public interface ISchemaCatalog
{
    IReadOnlyList<Schema> All { get; }

    /// <exception cref="KeyNotFoundException">Se lo schema non esiste.</exception>
    Schema Get(string id);
}
