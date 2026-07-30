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

                // Alias usati da ThemeHeadFont nei due temi (lotto-a-brief.md).
                // Limite noto e accettato: sono font variabili e MAUI su
                // Android non seleziona in modo affidabile i pesi interni, quindi
                // i titoli renderanno al peso di default (non 700/800): non è
                // un bug da aggirare con FontAttributes="Bold".
                fonts.AddFont("Unbounded-Variable.ttf", "Unbounded");
                fonts.AddFont("SpaceGrotesk-Variable.ttf", "SpaceGrotesk");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // La ViewModel deve ricevere LA connessione del container, non una
        // nuova: due istanze significherebbero una ViewModel iscritta a una
        // connessione diversa da quella che parla col server.
        builder.Services.AddSingleton<IGameConnection, SignalRGameConnection>();
        builder.Services.AddSingleton<IThemeStore, PreferencesThemeStore>();
        builder.Services.AddSingleton<IThemeService, ThemeService>();
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
