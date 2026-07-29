using FrasiSquisite.App.Pages;
using FrasiSquisite.App.Services;
using FrasiSquisite.App.ViewModels;
using Microsoft.Extensions.Logging;

namespace FrasiSquisite.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // La ViewModel deve ricevere LA connessione del container, non una
        // nuova: due istanze significherebbero una ViewModel iscritta a una
        // connessione diversa da quella che parla col server.
        builder.Services.AddSingleton<IGameConnection, SignalRGameConnection>();
        builder.Services.AddSingleton(sp => new GameSessionViewModel(
            sp.GetRequiredService<IGameConnection>(),
            PlayerIdentity.Current()));
        builder.Services.AddSingleton<GamePage>();

        return builder.Build();
    }
}

/// <summary>
/// Identità del giocatore: un GUID generato al primo avvio e conservato in
/// SecureStorage. Nessun account (spec §9).
/// </summary>
public static class PlayerIdentity
{
    private const string Key = "player-id";

    public static Guid Current()
    {
        // SecureStorage.GetAsync/SetAsync possono catturare il
        // SynchronizationContext corrente; bloccare su di essi con
        // GetAwaiter().GetResult() dallo stesso contesto (qui, durante
        // CreateMauiApp) è il classico scenario di deadlock di MAUI. Eseguendo
        // l'intero lavoro async dentro Task.Run, gira su un thread del thread
        // pool senza contesto catturato, quindi il blocco esterno è sicuro.
        return Task.Run(async () =>
        {
            var salvato = await SecureStorage.Default.GetAsync(Key);

            if (Guid.TryParse(salvato, out var esistente))
            {
                return esistente;
            }

            var nuovo = Guid.NewGuid();
            await SecureStorage.Default.SetAsync(Key, nuovo.ToString());
            return nuovo;
        }).GetAwaiter().GetResult();
    }
}
