using FrasiSquisite.Shared.Schemas;

namespace FrasiSquisite.Domain.Tests;

public static class TestSchemas
{
    /// <summary>Schema sintetico con K caselle, per i test di proprietà.</summary>
    public static Schema WithSlots(int k)
    {
        var caselle = Enumerable.Range(0, k)
            .Select(i => new Casella($"Ruolo{i}", $"Prompt {i}", $"Esempio {i}"))
            .ToList();

        var template = string.Join(" ", Enumerable.Range(0, k).Select(i => $"{{{i}}}"));

        return new Schema($"test-{k}", 1, $"Test {k}", caselle, template);
    }
}
