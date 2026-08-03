using System.Collections.Concurrent;
using System.Reflection;
using FrasiSquisite.Server.Realtime;
using Xunit;


namespace FrasiSquisite.Server.Tests.Realtime;

public class GameHostTests
{
    /// <summary>
    /// La tabella dei lucchetti protegge lo stato tenuto da IRoomRegistry, che
    /// vive nel container di dipendenze: deve avere lo stesso ambito, quindi
    /// essere un campo d'istanza.
    ///
    /// Da <c>static</c> sopravviveva al container ed era condivisa fra host
    /// diversi nello stesso processo. In produzione non si notava, perché
    /// GameHost è un singleton e di host ce n'è uno solo; nei test invece due
    /// host indipendenti che pescavano lo stesso codice stanza si
    /// serializzavano a vicenda pur non avendo alcuno stato in comune.
    ///
    /// Il test ispeziona il tipo invece di misurare i tempi di due dispatch
    /// concorrenti: la proprietà da difendere è esattamente "questo campo non
    /// è statico", e un test cronometrico sarebbe a sua volta intermittente —
    /// cioè proprio il difetto che questa correzione elimina.
    /// </summary>
    [Fact]
    public void LaTabellaDeiLucchettiEPerIstanzaENonStatica()
    {
        var campi = typeof(GameHost)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ConcurrentDictionary<string, SemaphoreSlim>))
            .ToList();

        var campo = Assert.Single(campi);

        Assert.False(
            campo.IsStatic,
            $"'{campo.Name}' è statico: la tabella dei lucchetti verrebbe condivisa fra host " +
            "diversi nello stesso processo, mentre le stanze che protegge vivono nel container.");
    }

    /// <summary>
    /// Due host distinti non devono condividere nulla. Se il campo tornasse
    /// statico questa uguaglianza diventerebbe vera, quindi l'asserzione
    /// inversa fallirebbe.
    /// </summary>
    [Fact]
    public void DueHostHannoTabelleDeiLucchettiDistinte()
    {
        var campo = typeof(GameHost)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(ConcurrentDictionary<string, SemaphoreSlim>));

        // Le dipendenze restano nulle di proposito: il costruttore primario non
        // le valida e il test non chiama nulla che le usi, gli serve solo che
        // gli inizializzatori di campo girino.
        var uno = new GameHost(null!, null!, null!, null!, null!);
        var due = new GameHost(null!, null!, null!, null!, null!);

        Assert.NotNull(campo.GetValue(uno));
        Assert.NotNull(campo.GetValue(due));
        Assert.NotSame(campo.GetValue(uno), campo.GetValue(due));
    }
}
