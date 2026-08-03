using FrasiSquisite.Server.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FrasiSquisite.Server.Tests.Ai;

public class AiConfigurazioneTests
{
    [Fact]
    public void SenzaChiaveLaConfigurazioneRisultaDisabilitata()
    {
        var opzioni = new AiOptions { ApiKey = "" };

        Assert.False(opzioni.Abilitato);
    }

    [Fact]
    public void ConLaChiaveLaConfigurazioneRisultaAbilitata()
    {
        var opzioni = new AiOptions { ApiKey = "sk-qualcosa" };

        Assert.True(opzioni.Abilitato);
    }

    /// <summary>
    /// Il degrado non e' un ramo condizionale sparso nel codice ma la scelta
    /// di quale implementazione registrare (spec §7). Il server di test non
    /// ha chiave configurata, quindi deve risolvere quella disabilitata.
    /// </summary>
    [Fact]
    public void SenzaChiaveIlContainerRisolveIlProviderDisabilitato()
    {
        using var factory = new WebApplicationFactory<Program>();

        var provider = factory.Services.GetRequiredService<IAiTextProvider>();

        Assert.IsType<DisabledAiTextProvider>(provider);
    }

    [Fact]
    public async Task IlProviderDisabilitatoRestituisceSempreNull()
    {
        var provider = new DisabledAiTextProvider();

        Assert.Null(await provider.CompletaAsync("sistema", "utente", CancellationToken.None));
    }

    /// <summary>
    /// Stessa logica del provider di testo: senza chiave configurata il
    /// server di test risolve il provider immagini disabilitato.
    /// </summary>
    [Fact]
    public void SenzaChiaveIlContainerRisolveIlProviderImmaginiDisabilitato()
    {
        using var factory = new WebApplicationFactory<Program>();

        var provider = factory.Services.GetRequiredService<IAiImageProvider>();

        Assert.IsType<DisabledAiImageProvider>(provider);
    }

    /// <summary>
    /// Con una chiave in configurazione, il container deve risolvere
    /// l'implementazione vera e non quella spenta.
    ///
    /// La chiave passa per <c>IWebHostBuilder.UseSetting</c> e non per una
    /// variabile d'ambiente di processo: in <c>Program.cs</c> la scelta del
    /// provider viene decisa leggendo <c>builder.Configuration</c> PRIMA di
    /// <c>builder.Build()</c>, mentre le configurazioni aggiunte da
    /// <c>WithWebHostBuilder(..).ConfigureAppConfiguration(..)</c> vengono
    /// applicate solo durante <c>Build()</c> stesso — troppo tardi, e infatti
    /// con quell'approccio il test risolveva ancora
    /// <see cref="DisabledAiImageProvider"/>. <c>UseSetting</c> scrive invece
    /// nella configurazione dell'host, disponibile già prima di
    /// <c>Build()</c>, senza toccare l'ambiente del processo: l'effetto resta
    /// confinato a questa singola <see cref="WebApplicationFactory{Program}"/>
    /// e nessun altro test (es. la garanzia "senza modello" in
    /// GameHubTests, che gira in parallelo su un'altra classe) può vederlo.
    /// </summary>
    [Fact]
    public void ConLaChiaveIlContainerRisolveOpenAiCompatibleImageProvider()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Ai:ApiKey", "chiave-di-prova-mai-reale"));

        var provider = factory.Services.GetRequiredService<IAiImageProvider>();

        Assert.IsType<OpenAiCompatibleImageProvider>(provider);
    }
}
