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

        // GetManifestResourceNames() non garantisce alcun ordine: con un solo
        // schema era innocuo, ma con più di uno il selettore in lobby
        // mostrerebbe un ordine arbitrario, potenzialmente diverso da una
        // build all'altra. Ordine deterministico: il classico per primo (è il
        // default), gli altri in ordine alfabetico di nome.
        //
        // SingleOrDefault + messaggio esplicito invece di .Single(): se lo
        // schema di default sparisse dagli embedded (rinominato o rimosso),
        // .Single() farebbe fallire la DI del server con un bare
        // InvalidOperationException "Sequence contains no matching element",
        // senza dire quale schema manca. Stesso testo di Get() qui sotto, per
        // coerenza: un catalogo senza il suo default è comunque un errore
        // "schema non trovato".
        var predefinito = schemi.SingleOrDefault(s => s.Id == Schema.DefaultId)
            ?? throw new InvalidOperationException($"Schema '{Schema.DefaultId}' non trovato.");
        All = [
            predefinito,
            .. schemi.Where(s => s.Id != Schema.DefaultId).OrderBy(s => s.Nome, StringComparer.Ordinal),
        ];
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
