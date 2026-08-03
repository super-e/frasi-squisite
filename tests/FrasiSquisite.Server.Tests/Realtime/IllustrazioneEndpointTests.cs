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
}
