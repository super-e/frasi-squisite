using FrasiSquisite.Domain.Engine;
using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Server.Realtime;
using FrasiSquisite.Server.Rooms;
using FrasiSquisite.Shared.Schemas;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

builder.Services.AddSingleton<ISchemaCatalog, EmbeddedSchemaCatalog>();
builder.Services.AddSingleton<IRandomSource, SystemRandomSource>();
builder.Services.AddSingleton<IWordPool, StaticWordPool>();
builder.Services.AddSingleton<IGameMode, RoleSchemaMode>();
builder.Services.AddSingleton<IGameEngine, GameEngine>();
builder.Services.AddSingleton<RoomCodeGenerator>();
builder.Services.AddSingleton<IRoomRegistry, RoomRegistry>();
builder.Services.AddSingleton<GameHost>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<GameHub>("/hubs/game");

app.Run();

public partial class Program;
