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
builder.Services.AddSingleton<IWordPool, StaticWordPool>();
builder.Services.AddSingleton<IGameMode, RoleSchemaMode>();
builder.Services.AddSingleton<IGameEngine, GameEngine>();
builder.Services.AddSingleton<RoomCodeGenerator>();
builder.Services.AddSingleton<IRoomRegistry, RoomRegistry>();
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
        // IllustrationRunner creano con TimeoutSeconds e ImageTimeoutSeconds:
        // questo qui limita solo la richiesta HTTP di
        // OpenAiCompatibleTextProvider, un dettaglio di QUESTA implementazione.
        // Quelli dei runner sono il limite a livello di contratto - valgono
        // per qualunque IAiTextProvider, presente o futuro, HTTP o no - ed e'
        // cio' che rende testabile ciascun timeout (RefinementRunnerTests.
        // OltreIlTimeoutSiRestituisceNull, IllustrationRunnerTests) con un
        // doppio finto e senza HttpClient. Chi impone davvero il limite di
        // ciascuna operazione e' quindi sempre il runner, non questo client.
        //
        // Il valore qui pero' deve essere il PIU' GRANDE dei due
        // (ImageTimeoutSeconds, che oggi e' 90 contro i 10 di TimeoutSeconds):
        // questo stesso HttpClient e' condiviso dal primo passo
        // dell'illustrazione (la traduzione, guidata dal token a
        // ImageTimeoutSeconds di IllustrationRunner). Impostarlo al piu'
        // piccolo dei due tronca la traduzione a dieci secondi indipendentemente
        // da quanto il runner sia disposto ad aspettare, e il fallimento del
        // trasporto diventa - per contratto di IAiTextProvider - un null muto,
        // senza eccezioni e senza test rossi: l'illustrazione fallirebbe ogni
        // volta che il modello supera i dieci secondi, cosa tutt'altro che
        // rara con un modello di ragionamento. La rifinitura non ne risente:
        // resta comunque tagliata a TimeoutSeconds dal proprio token in
        // RefinementRunner, che e' il limite vero e quello provato dai test.
        // Questo timeout di trasporto resta solo una rete di sicurezza grossolana
        // per il caso (oggi non previsto) in cui nessun token la governi.
        c.Timeout = TimeSpan.FromSeconds(Math.Max(aiOptions.TimeoutSeconds, aiOptions.ImageTimeoutSeconds));
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

        // Non TimeoutSeconds: quello è il limite della rifinitura, dieci
        // secondi, e generare un'immagine ne richiede molti di più.
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
