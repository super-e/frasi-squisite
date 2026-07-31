using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Schemas;
using FrasiSquisite.Shared.Validation;
using Xunit;

namespace FrasiSquisite.Domain.Tests.Filling;

public class StaticWordPoolTests
{
    private readonly IWordPool _pool = new StaticWordPool();

    public static IEnumerable<object[]> RuoliDelCatalogo() =>
        new EmbeddedSchemaCatalog().All
            .SelectMany(s => s.Caselle.Select(c => c.Ruolo))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ruolo => new object[] { ruolo });

    /// <summary>
    /// Il fallback generico esiste per i ruoli che il dizionario non conosce,
    /// non per quelli che compaiono negli schemi: se uno schema del catalogo
    /// finisce lì, in partita il bot scrive "qualcosa" e "chissà" al posto di
    /// una parola sensata, ed è indistinguibile da un bot rotto.
    ///
    /// Il test è parametrico sul catalogo, non su una lista scritta a mano:
    /// aggiungere uno schema con ruoli nuovi e scordarsi le parole diventa un
    /// test rosso invece di una partita deludente.
    /// </summary>
    [Theory]
    [MemberData(nameof(RuoliDelCatalogo))]
    public void OgniRuoloDiOgniSchemaHaParoleDedicate(string ruolo)
    {
        Assert.True(
            StaticWordPool.RuoliCoperti.Contains(ruolo, StringComparer.OrdinalIgnoreCase),
            $"il ruolo '{ruolo}' non ha parole dedicate: il bot ricadrebbe sul generico.");
    }

    [Theory]
    [MemberData(nameof(RuoliDelCatalogo))]
    public void RestituisceUnaParolaPerIRuoliNoti(string ruolo)
    {
        var parola = _pool.Take(ruolo, new SeededRandomSource(1));

        Assert.False(string.IsNullOrWhiteSpace(parola));
    }

    [Fact]
    public void PerUnRuoloSconosciutoRicadeSuUnaListaGenerica()
    {
        var parola = _pool.Take("RuoloCheNonEsiste", new SeededRandomSource(1));

        Assert.False(string.IsNullOrWhiteSpace(parola));
    }

    /// <summary>
    /// Il motore riapplica la validazione a ogni casella: se il dizionario
    /// contenesse una parola non valida, il riempimento del bot fallirebbe in
    /// partita e non qui. Il limite è 60 caratteri, e le voci degli schemi
    /// narrativi sono frasi intere ("Non è colpa mia, io ho solo firmato"),
    /// non parole singole: qui ci si va molto più vicino che con "distratto".
    /// </summary>
    [Theory]
    [MemberData(nameof(RuoliDelCatalogo))]
    [InlineData("RuoloCheNonEsiste")]
    public void OgniParolaDelDizionarioSuperaLaValidazione(string ruolo)
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var parola = _pool.Take(ruolo, new SeededRandomSource(seed));

            Assert.True(SlotTextValidator.Validate(parola).IsValid, $"parola non valida: '{parola}'");
        }
    }

    /// <summary>
    /// Take() sceglie con random.Next(parole.Length): una voce oltre il limite
    /// resterebbe invisibile finché un seed non la pesca. Il test sopra prova
    /// 50 seed, questo le guarda tutte — e la validazione normalizza il testo,
    /// quindi si misura ciò che il motore accetterebbe davvero.
    /// </summary>
    [Theory]
    [MemberData(nameof(RuoliDelCatalogo))]
    public void NessunaVoceDelDizionarioSforaIlLimiteDiCaratteri(string ruolo)
    {
        var viste = new HashSet<string>(StringComparer.Ordinal);
        for (var seed = 0; seed < 500; seed++)
        {
            viste.Add(_pool.Take(ruolo, new SeededRandomSource(seed)));
        }

        Assert.All(viste, voce => Assert.True(
            SlotTextValidator.Validate(voce).Normalized.Length <= SlotTextValidator.MaxLength,
            $"'{voce}' supera i {SlotTextValidator.MaxLength} caratteri ({voce.Length})."));
    }

    [Fact]
    public void ConLoStessoSeedRestituisceLaStessaParola()
    {
        Assert.Equal(
            _pool.Take("Soggetto", new SeededRandomSource(42)),
            _pool.Take("Soggetto", new SeededRandomSource(42)));
    }
}
