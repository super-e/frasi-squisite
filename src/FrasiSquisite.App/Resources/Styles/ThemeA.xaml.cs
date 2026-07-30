namespace FrasiSquisite.App.Resources.Styles;

/// <summary>
/// Tema "a" — Surrealista Pop. Nessuna logica: solo i token del tema (vedi
/// ThemeA.xaml). Il code-behind esiste solo perché IThemeService deve poter
/// scrivere <c>new ThemeA()</c> per scambiarlo a runtime con <see cref="ThemeB"/>.
/// </summary>
public partial class ThemeA : ResourceDictionary
{
    public ThemeA()
    {
        InitializeComponent();
    }
}
