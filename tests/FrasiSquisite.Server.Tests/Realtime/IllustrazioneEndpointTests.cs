using System.Net;
using FrasiSquisite.Server.Images;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FrasiSquisite.Server.Tests.Realtime;

public class IllustrazioneEndpointTests(WebApplicationFactory<Program> fabbrica)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task UnIdentificativoValidoServeIByte()
    {
        var deposito = fabbrica.Services.GetRequiredService<ImageStore>();
        var percorso = deposito.Salva([9, 8, 7]);

        var risposta = await fabbrica.CreateClient().GetAsync(percorso);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Equal("image/png", risposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal<byte[]>([9, 8, 7], await risposta.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UnIdentificativoInventatoDa404()
    {
        var risposta = await fabbrica.CreateClient().GetAsync("/illustrazioni/inventato");

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    /// <summary>
    /// Il comportamento con identificativi ostili è già corretto per
    /// costruzione: TryGet è un lookup su dizionario in memoria, non tocca
    /// mai il filesystem, quindi la traversal del percorso non è nemmeno un
    /// problema che può porsi. Ma è una garanzia che va dimostrata, non solo
    /// dedotta — è il genere di cosa che una futura cache su disco potrebbe
    /// rompere senza che nessuno se ne accorga. Ogni caso deve rispondere
    /// "non c'è" (404), non esplodere (500) o peggio ancora servire qualcosa.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..%2f..%2fetc%2fpasswd")]
    [InlineData("..\\..\\windows\\win.ini")]
    [InlineData("con")]
    [InlineData("id-con-'-apici-e-\"-virgolette")]
    public async Task UnIdentificativoConCaratteriDiPercorsoDa404(string identificativoOstile)
    {
        var risposta = await fabbrica.CreateClient().GetAsync($"/illustrazioni/{identificativoOstile}");

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    [Fact]
    public async Task UnIdentificativoMoltoLungoDa404()
    {
        var identificativoLunghissimo = new string('a', 100_000);

        var risposta = await fabbrica.CreateClient().GetAsync($"/illustrazioni/{identificativoLunghissimo}");

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }
}
