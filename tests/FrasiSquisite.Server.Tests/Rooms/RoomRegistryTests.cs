using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Server.Rooms;
using FrasiSquisite.Shared.Schemas;
using Xunit;

namespace FrasiSquisite.Server.Tests.Rooms;

public class RoomCodeGeneratorTests
{
    [Fact]
    public void GeneraCodiciDellaLunghezzaAttesa()
    {
        var generatore = new RoomCodeGenerator(new SeededRandomSource(42));

        Assert.Equal(RoomCodeGenerator.CodeLength, generatore.Next().Length);
    }

    [Fact]
    public void UsaSoloCaratteriDellAlfabetoSenzaAmbiguita()
    {
        var generatore = new RoomCodeGenerator(new SeededRandomSource(7));

        for (var i = 0; i < 200; i++)
        {
            Assert.All(generatore.Next(), c => Assert.Contains(c, RoomCodeGenerator.Alphabet));
        }
    }

    [Fact]
    public void ConLoStessoSeedProduceLaStessaSequenza()
    {
        var uno = new RoomCodeGenerator(new SeededRandomSource(99));
        var due = new RoomCodeGenerator(new SeededRandomSource(99));

        Assert.Equal(uno.Next(), due.Next());
    }
}

public class RoomRegistryTests
{
    private static RoomRegistry Registro(int seed = 1) =>
        new(new RoomCodeGenerator(new SeededRandomSource(seed)), new EmbeddedSchemaCatalog());

    [Fact]
    public void CreaUnaStanzaInLobbyConLoSchemaPredefinito()
    {
        var stato = Registro().Create();

        Assert.Equal(Schema.DefaultId, stato.Schema.Id);
        Assert.Empty(stato.Players);
    }

    [Fact]
    public void LaStanzaCreataERecuperabilePerCodice()
    {
        var registro = Registro();
        var creata = registro.Create();

        Assert.True(registro.TryGet(creata.RoomCode, out var trovata));
        Assert.Equal(creata.RoomCode, trovata.RoomCode);
    }

    [Fact]
    public void UnCodiceInesistenteNonSiTrova()
    {
        Assert.False(Registro().TryGet("ZZZZ", out _));
    }

    [Fact]
    public void IlCodiceSiCercaSenzaDistinzioneDiMaiuscole()
    {
        var registro = Registro();
        var creata = registro.Create();

        Assert.True(registro.TryGet(creata.RoomCode.ToLowerInvariant(), out _));
    }

    [Fact]
    public void SetSostituisceLoStatoDellaStanza()
    {
        var registro = Registro();
        var creata = registro.Create();

        registro.Set(creata.RoomCode, creata with { HostId = Guid.NewGuid() });

        Assert.True(registro.TryGet(creata.RoomCode, out var aggiornata));
        Assert.NotEqual(Guid.Empty, aggiornata.HostId);
    }

    [Fact]
    public void RemoveEliminaLaStanza()
    {
        var registro = Registro();
        var creata = registro.Create();

        registro.Remove(creata.RoomCode);

        Assert.False(registro.TryGet(creata.RoomCode, out _));
    }

    [Fact]
    public void CreareTanteStanzeNonProduceCodiciDuplicati()
    {
        var registro = Registro();

        var codici = Enumerable.Range(0, 500).Select(_ => registro.Create().RoomCode).ToList();

        Assert.Equal(codici.Count, codici.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
