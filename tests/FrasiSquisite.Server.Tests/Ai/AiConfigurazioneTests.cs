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
    /// La chiave passa per una variabile d'ambiente e non per
    /// <c>WithWebHostBuilder(..).ConfigureAppConfiguration(..)</c>: in
    /// <c>Program.cs</c> la scelta del provider viene decisa leggendo
    /// <c>builder.Configuration</c> PRIMA di <c>builder.Build()</c>, mentre le
    /// configurazioni aggiunte da <c>WithWebHostBuilder</c> vengono applicate
    /// solo durante <c>Build()</c> stesso — troppo tardi, e infatti con
    /// quell'approccio il test risolveva ancora <see cref="DisabledAiImageProvider"/>.
    /// Le variabili d'ambiente, invece, fanno parte della configurazione fin
    /// da <c>WebApplication.CreateBuilder(args)</c>, quindi sono già presenti
    /// quando <c>Program.cs</c> legge <c>aiOptions.Abilitato</c>.
    /// </summary>
    [Fact]
    public void ConLaChiaveIlContainerRisolveOpenAiCompatibleImageProvider()
    {
        const string chiave = "Ai__ApiKey";
        var precedente = Environment.GetEnvironmentVariable(chiave);
        try
        {
            Environment.SetEnvironmentVariable(chiave, "chiave-di-prova-mai-reale");

            using var factory = new WebApplicationFactory<Program>();
            var provider = factory.Services.GetRequiredService<IAiImageProvider>();

            Assert.IsType<OpenAiCompatibleImageProvider>(provider);
        }
        finally
        {
            // La variabile è di processo: va tolta subito, altrimenti
            // "accenderebbe" l'AI anche per gli altri test che condividono lo
            // stesso processo di test (es. la garanzia "senza modello" in
            // GameHubTests).
            Environment.SetEnvironmentVariable(chiave, precedente);
        }
    }
}
