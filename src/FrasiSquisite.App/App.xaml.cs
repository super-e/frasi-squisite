using FrasiSquisite.App.Resources.Styles;
using FrasiSquisite.App.Services;

namespace FrasiSquisite.App;

public partial class App : Application
{
	private readonly IThemeService _themeService;

	public App(IThemeService themeService)
	{
		InitializeComponent();

		_themeService = themeService;

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
		return new Window(new AppShell());
	}
}