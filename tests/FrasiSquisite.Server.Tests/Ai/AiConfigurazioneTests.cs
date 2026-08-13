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

    /// <summary>
    /// Il primo passo dell'illustrazione (la traduzione) passa per
    /// IAiTextProvider, il cui HttpClient qui in Program.cs deve restare
    /// sopra al tetto vero della rifinitura (TimeoutMassimoSecondi, non
    /// TimeoutSeconds che è solo la base - design 2026-08-12 "migliora la
    /// rifinitura"). IllustrationRunner governa l'intera operazione a due
    /// passi con ImageTimeoutSeconds (di norma 90s), ma quel token non serve
    /// a nulla se il trasporto tronca già la prima chiamata prima che la
    /// rifinitura abbia mai il tempo di raggiungere il proprio tetto. Il
    /// client va quindi impostato sul più grande dei due TETTI, cosicché il
    /// trasporto resti una rete di sicurezza sotto ad entrambi e non un
    /// collo di bottiglia sopra di uno dei due.
    /// </summary>
    [Fact]
    public void IlClientDelProviderDiTestoUsaIlPiuGrandeDeiDueTetti()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Ai:ApiKey", "chiave-di-prova-mai-reale")
                .UseSetting("Ai:TimeoutMassimoSecondi", "5")
                .UseSetting("Ai:ImageTimeoutSeconds", "20"));

        var httpClientFactory = factory.Services.GetRequiredService<IHttpClientFactory>();
        var client = httpClientFactory.CreateClient(nameof(IAiTextProvider));

        Assert.Equal(TimeSpan.FromSeconds(20), client.Timeout);
    }

    /// <summary>
    /// Verso opposto del test precedente: con una partita numerosa il tetto
    /// della rifinitura può superare ImageTimeoutSeconds, e il client deve
    /// seguirlo - altrimenti il trasporto tornerebbe a essere il collo di
    /// bottiglia proprio nel caso che questo lotto doveva risolvere.
    /// </summary>
    [Fact]
    public void IlClientDelProviderDiTestoSeguIlTettoDellaRifinituraQuandoEPiuGrande()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Ai:ApiKey", "chiave-di-prova-mai-reale")
                .UseSetting("Ai:TimeoutMassimoSecondi", "25")
                .UseSetting("Ai:ImageTimeoutSeconds", "20"));

        var httpClientFactory = factory.Services.GetRequiredService<IHttpClientFactory>();
        var client = httpClientFactory.CreateClient(nameof(IAiTextProvider));

        Assert.Equal(TimeSpan.FromSeconds(25), client.Timeout);
    }
}
