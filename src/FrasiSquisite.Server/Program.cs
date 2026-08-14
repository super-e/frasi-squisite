using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Server.Ai;
using FrasiSquisite.Server.Images;
using FrasiSquisite.Server.Realtime;
using FrasiSquisite.Server.Rooms;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Schemas;

var builder = WebApplication.CreateBuilder(args);

// Il client (e i test) deserializzano con ProtocolJson.Options: senza questa
// configurazione esplicita, un domani un converter aggiunto lì non verrebbe
// applicato in serializzazione, e il client leggerebbe male in silenzio.
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions = ProtocolJson.Options);

builder.Services.AddSingleton<ISchemaCatalog, EmbeddedSchemaCatalog>();
builder.Services.AddSingleton<IRandomSource, SystemRandomSource>();
builder.Services.AddSingleton<StaticWordPool>();
builder.Services.AddSingleton<CachedAiWordPool>();
builder.Services.AddSingleton<IWordPool>(sp => sp.GetRequiredService<CachedAiWordPool>());
builder.Services.AddSingleton<BotWordPoolRunner>();
builder.Services.AddHostedService<BotWordPoolWarmupService>();
builder.Services.AddSingleton<IGameMode, RoleSchemaMode>();
builder.Services.AddSingleton<IGameEngine, GameEngine>();
builder.Services.AddSingleton<RoomCodeGenerator>();
builder.Services.AddSingleton<IRoomRegistry, RoomRegistry>();
builder.Services.AddSingleton<IGracePeriodTimer, RealGracePeriodTimer>();
builder.Services.AddSingleton<GameHost>();
builder.Services.AddSingleton<RefinementRunner>();
builder.Services.AddSingleton<IllustrationRunner>();
builder.Services.AddSingleton<ImageStore>();

builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.Sezione));

// Quale implementazione registrare e' L'UNICO punto in cui si decide se l'AI
// e' accesa. Da qui in poi nessun altro file conosce quella distinzione.
var aiOptions = builder.Configuration.GetSection(AiOptions.Sezione).Get<AiOptions>() ?? new AiOptions();

if (aiOptions.Abilitato)
{
    builder.Services.AddHttpClient<IAiTextProvider, OpenAiCompatibleTextProvider>(c =>
    {
        c.BaseAddress = new Uri(aiOptions.BaseUrl);
        c.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aiOptions.ApiKey);

        // Non è un doppione dei CancellationTokenSource che RefinementRunner e
        // IllustrationRunner creano: quello di RefinementRunner e' un valore
        // calcolato (base piu' un incremento per frase, con tetto a
        // TimeoutMassimoSecondi), quello di IllustrationRunner e'
        // ImageTimeoutSeconds. Questo qui limita solo la richiesta HTTP di
        // OpenAiCompatibleTextProvider, un dettaglio di QUESTA
        // implementazione. Quelli dei runner sono il limite a livello di
        // contratto - valgono per qualunque IAiTextProvider, presente o
        // futuro, HTTP o no - ed e' cio' che rende testabile ciascun timeout
        // (RefinementRunnerTests.OltreIlTimeoutSiRestituisceNull,
        // IllustrationRunnerTests) con un doppio finto e senza HttpClient.
        // Chi impone davvero il limite di ciascuna operazione e' quindi
        // sempre il runner, non questo client.
        //
        // Il valore qui pero' deve essere il PIU' GRANDE dei due TETTI, non
        // delle due basi: la rifinitura puo' arrivare fino a
        // TimeoutMassimoSecondi (oggi 30, non TimeoutSeconds che e' solo il
        // punto di partenza a 15) con molte frasi nello stesso giro. Se
        // ImageTimeoutSeconds scendesse mai sotto il tetto della rifinitura,
        // usare TimeoutSeconds qui sarebbe silenziosamente sbagliato: il
        // trasporto troncherebbe la richiesta prima che il token del runner
        // scada davvero, e il fallimento diventa - per contratto di
        // IAiTextProvider - un null muto, senza eccezioni e senza test rossi
        // (AiConfigurazioneTests.
        // IlClientDelProviderDiTestoSegueIlTettoDellaRifinituraQuandoEPiuGrande
        // e' il test che lo pinza: l'altro, con TimeoutMassimoSecondi sotto
        // ImageTimeoutSeconds, passerebbe anche con la formula sbagliata).
        // Questo stesso HttpClient e' anche condiviso dal primo passo
        // dell'illustrazione (la traduzione, guidata dal token a
        // ImageTimeoutSeconds di IllustrationRunner): con ImageTimeoutSeconds
        // a 90, oggi resta comunque il piu' grande dei due tetti.
        // Questo timeout di trasporto resta solo una rete di sicurezza
        // grossolana per il caso (oggi non previsto) in cui nessun token la
        // governi.
        c.Timeout = TimeSpan.FromSeconds(Math.Max(aiOptions.TimeoutMassimoSecondi, aiOptions.ImageTimeoutSeconds));
    });

    builder.Services.AddHttpClient<IAiImageProvider, OpenAiCompatibleImageProvider>(c =>
    {
        c.BaseAddress = new Uri(aiOptions.BaseUrl);

        // Niente Authorization qui, apposta: questo client serve solo la
        // generazione (vedi OpenAiCompatibleImageProvider, che aggiunge la
        // chiave esplicitamente su quella richiesta). Il download va da un
        // indirizzo scelto dal fornitore, non da noi, e usa un HttpClient
        // separato proprio per non ereditare mai questo header — se un
        // domani qualcuno lo rimettesse qui "per semplificare", la chiave
        // tornerebbe a seguire il download verso qualunque host.

        // Non TimeoutSeconds: quello è la base del limite della rifinitura
        // (fino a TimeoutMassimoSecondi con molte frasi), e generare
        // un'immagine ne richiede molti di più.
        c.Timeout = TimeSpan.FromSeconds(aiOptions.ImageTimeoutSeconds);
    });
}
else
{
    builder.Services.AddSingleton<IAiTextProvider, DisabledAiTextProvider>();
    builder.Services.AddSingleton<IAiImageProvider, DisabledAiImageProvider>();
}

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Non passa da SignalR: un'immagine è un file, e il trasporto del gioco è per
// messaggi piccoli. L'identificativo nel percorso è l'unica credenziale, il
// che rende l'indirizzo condivisibile di proposito — chi ce l'ha, vede.
app.MapGet("/illustrazioni/{id}", (string id, ImageStore deposito) =>
    deposito.TryGet(id, out var byteImmagine)
        ? Results.File(byteImmagine, "image/png")
        : Results.NotFound());

app.MapHub<GameHub>("/hubs/game");

app.Run();

public partial class Program;
