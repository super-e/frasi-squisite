using FrasiSquisite.App.Resources.Styles;
using FrasiSquisite.App.Services;
using FrasiSquisite.App.ViewModels;

namespace FrasiSquisite.App;

public partial class App : Application
{
	private readonly IThemeService _themeService;
	private readonly GameSessionViewModel _gameSession;

	public App(IThemeService themeService, GameSessionViewModel gameSession)
	{
		InitializeComponent();

		_themeService = themeService;
		_gameSession = gameSession;

		// Unico punto che tocca Application.Current.Resources per il tema:
		// vincolo tecnico del lotto (vedi lotto-a-brief.md), non un dettaglio.
		// ThemeService non conosce MAUI e non può farlo da sé; qui reagiamo
		// allo stesso evento sia al primo avvio (ApplyInitial, sotto) sia a
		// ogni cambio da Impostazioni, cosicché il percorso sia uno solo. Ogni
		// riferimento a un token di tema in XAML deve essere {DynamicResource},
		// mai {StaticResource}: è lo scambio di dizionario qui sotto che rende
		// visibile un cambio di tema senza riavviare l'app, e solo
		// {DynamicResource} si accorge che il dizionario è cambiato.
		_themeService.ThemeChanged += ApplicaTema;
		_themeService.ApplyInitial();
	}

	private void ApplicaTema(ThemeChoice tema)
	{
		var dizionari = Resources.MergedDictionaries;
		var precedente = dizionari.FirstOrDefault(d => d is ThemeA or ThemeB);
		if (precedente is not null)
		{
			dizionari.Remove(precedente);
		}

		dizionari.Add(tema == ThemeChoice.SurrealistaPop
			? new ThemeA()
			: new ThemeB());
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());

		// .WithAutomaticReconnect() si arrende dopo circa 42s di tentativi
		// (ritardi di default 0/2/10/30s): un telefono rimasto in sospensione
		// più a lungo torna in foreground con la connessione già Disconnected,
		// non Reconnecting, quindi nessun evento di trasporto farebbe mai
		// scattare OnReconnected da solo (design rientro §5.3). TryRejoinAsync
		// è già un no-op silenzioso se non c'è nulla da rientrare o se la
		// connessione va comunque ristabilita da sé.
		window.Resumed += (_, _) => _ = _gameSession.TryRejoinAsync();

		return window;
	}
}