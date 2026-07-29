using FrasiSquisite.Shared.Schemas;
using Xunit;

namespace FrasiSquisite.Shared.Tests.Schemas;

public class EmbeddedSchemaCatalogTests
{
    private readonly ISchemaCatalog _catalogo = new EmbeddedSchemaCatalog();

    [Fact]
    public void CaricaLoSchemaSurrealistaClassico()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        Assert.Equal("surrealista-classico", schema.Id);
        Assert.Equal("Surrealista classico", schema.Nome);
        Assert.Equal(5, schema.SlotCount);
    }

    [Fact]
    public void OgniCasellaHaRuoloPromptEdEsempioNonVuoti()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        Assert.All(schema.Caselle, casella =>
        {
            Assert.False(string.IsNullOrWhiteSpace(casella.Ruolo));
            Assert.False(string.IsNullOrWhiteSpace(casella.Prompt));
            Assert.False(string.IsNullOrWhiteSpace(casella.Esempio));
        });
    }

    [Fact]
    public void ComponeLaFraseSecondoIlTemplate()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        var frase = schema.Compose(["Il cadavere", "squisito", "berrà", "il vino", "nuovo"]);

        Assert.Equal("Il cadavere squisito berrà il vino nuovo", frase);
    }

    [Fact]
    public void ComporreConUnNumeroSbagliatoDiValoriFallisce()
    {
        var schema = _catalogo.Get(Schema.DefaultId);

        Assert.Throws<ArgumentException>(() => schema.Compose(["uno", "due"]));
    }

    [Fact]
    public void ChiedereUnoSchemaInesistenteFallisceConMessaggioUtile()
    {
        var eccezione = Assert.Throws<KeyNotFoundException>(() => _catalogo.Get("non-esiste"));

        Assert.Contains("non-esiste", eccezione.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IlCatalogoEspoheTuttiGliSchemiCaricati()
    {
        Assert.NotEmpty(_catalogo.All);
        Assert.Contains(_catalogo.All, s => s.Id == Schema.DefaultId);
    }
}
