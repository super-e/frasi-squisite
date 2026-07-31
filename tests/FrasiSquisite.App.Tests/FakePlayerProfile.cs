using FrasiSquisite.App.Services;

namespace FrasiSquisite.App.Tests;

/// <summary>
/// Sostituto in memoria di <see cref="IPlayerProfile"/> per i test della
/// ViewModel: nessuna persistenza reale, solo il valore corrente e la lista
/// di quanto è stato salvato. In produzione lo implementa
/// <c>PreferencesPlayerProfile</c> (Preferences, MAUI).
/// </summary>
public sealed class FakePlayerProfile : IPlayerProfile
{
    public string Nickname { get; set; } = string.Empty;

    public List<string> Salvati { get; } = [];

    public void SaveNickname(string nickname)
    {
        Nickname = nickname;
        Salvati.Add(nickname);
    }
}
