using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace FrasiSquisite.Shared.Schemas;

/// <summary>
/// Legge gli schemi dai JSON embedded nell'assembly. In una fase successiva
/// affiancherà (non sostituirà) un catalogo servito dal server.
/// </summary>
public sealed class EmbeddedSchemaCatalog : ISchemaCatalog
{
    private const string ResourcePrefix = "FrasiSquisite.Shared.Schemas.Data.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ImmutableDictionary<string, Schema> _perId;

    public EmbeddedSchemaCatalog()
    {
        var assembly = typeof(EmbeddedSchemaCatalog).Assembly;
        var schemi = assembly.GetManifestResourceNames()
            .Where(nome => nome.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && nome.EndsWith(".json", StringComparison.Ordinal))
            .Select(nome => Leggi(assembly, nome))
            .ToList();

        All = [.. schemi];
        _perId = schemi.ToImmutableDictionary(s => s.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<Schema> All { get; }

    public Schema Get(string id) =>
        _perId.TryGetValue(id, out var schema)
            ? schema
            : throw new KeyNotFoundException($"Schema '{id}' non trovato.");

    private static Schema Leggi(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Risorsa '{resourceName}' non leggibile.");

        return JsonSerializer.Deserialize<Schema>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Risorsa '{resourceName}' contiene JSON nullo.");
    }
}
