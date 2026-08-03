using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Server.Ai;
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

        // Non è un doppione del CancellationTokenSource che RefinementRunner
        // crea con lo stesso TimeoutSeconds: questo qui limita solo la
        // richiesta HTTP di OpenAiCompatibleTextProvider, un dettaglio di
        // QUESTA implementazione. Quello di RefinementRunner e' il limite a
        // livello di contratto - vale per qualunque IAiTextProvider,
        // presente o futuro, HTTP o no - ed e' cio' che rende testabile il
        // timeout (RefinementRunnerTests.OltreIlTimeoutSiRestituisceNull) con
        // un doppio finto e senza HttpClient. Oggi coincidono perche' la
        // fonte e' la stessa; se un domani divergessero, entrambi restano
        // necessari.
        c.Timeout = TimeSpan.FromSeconds(aiOptions.TimeoutSeconds);
    });
}
else
{
    builder.Services.AddSingleton<IAiTextProvider, DisabledAiTextProvider>();
}

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<GameHub>("/hubs/game");

app.Run();

public partial class Program;
