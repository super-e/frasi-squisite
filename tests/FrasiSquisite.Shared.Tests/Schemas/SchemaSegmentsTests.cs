using FrasiSquisite.Shared.Schemas;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Schemas;

public class SchemaSegmentsTests
{
    private static Schema ConCaselle(int k, string template) =>
        new(
            "test",
            1,
            "Test",
            [.. Enumerable.Range(0, k).Select(i => new Casella($"Ruolo{i}", $"Prompt{i}", $"Esempio{i}"))],
            template);

    [Fact]
    public void UnTemplateDiSoleCaselleProduceSoloSegmentiDiCasella()
    {
        var schema = ConCaselle(3, "{0}{1}{2}");

        Assert.Equal(3, schema.Segments.Count);
        Assert.All(schema.Segments, s => Assert.True(s.IsSlot));
        Assert.Equal([0, 1, 2], schema.Segments.Select(s => s.SlotIndex));
    }

    [Fact]
    public void UnTemplateConSpaziIntercalaSegmentiLetteraliFraLeCaselle()
    {
        var schema = ConCaselle(2, "{0} {1}");

        Assert.Equal(3, schema.Segments.Count);
        Assert.Equal((true, 0), (schema.Segments[0].IsSlot, schema.Segments[0].SlotIndex));
        Assert.Equal((false, " "), (schema.Segments[1].IsSlot, schema.Segments[1].Literal));
        Assert.Equal((true, 1), (schema.Segments[2].IsSlot, schema.Segments[2].SlotIndex));
    }

    [Fact]
    public void IlTestoFissoPrimaDellaPrimaCasellaEDopoLUltimaDiventaUnSegmentoLetterale()
    {
        var schema = ConCaselle(1, "Dice: «{0}».");

        Assert.Equal(3, schema.Segments.Count);
        Assert.Equal((false, "Dice: «"), (schema.Segments[0].IsSlot, schema.Segments[0].Literal));
        Assert.Equal((true, 0), (schema.Segments[1].IsSlot, schema.Segments[1].SlotIndex));
        Assert.Equal((false, "»."), (schema.Segments[2].IsSlot, schema.Segments[2].Literal));
    }

    [Fact]
    public void LoSchemaDiDefaultProduceIlTessutoConnettivoAtteso()
    {
        var catalogo = new EmbeddedSchemaCatalog();
        var schema = catalogo.Get(Schema.DefaultId);

        var letterali = schema.Segments.Where(s => !s.IsSlot).Select(s => s.Literal).ToList();

        Assert.Contains(", dicendo: «", letterali);
        Assert.Contains("». La gente dice: «", letterali);
        Assert.Contains("», ed è andata a finire che ", letterali);
        Assert.Equal(8, schema.Segments.Count(s => s.IsSlot));
    }
}
