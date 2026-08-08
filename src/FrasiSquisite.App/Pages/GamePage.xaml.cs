using FrasiSquisite.App.ViewModels;

namespace FrasiSquisite.App.Pages;

public partial class GamePage : ContentPage
{
    private readonly GameSessionViewModel _viewModel;

    public GamePage(GameSessionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    // Copre l'avvio a freddo (design rientro §5.2): GamePage è l'unica
    // ShellContent dell'app (AppShell.xaml), quindi OnAppearing scatta una
    // volta all'avvio. TryRejoinAsync è già no-op silenzioso se non c'è
    // nulla da rientrare, quindi non serve guardia in più qui.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.TryRejoinAsync();
    }
}
