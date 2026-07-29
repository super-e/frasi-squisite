namespace FrasiSquisite.Shared.Protocol;

/// <summary>
/// Con distribuzione via APK i client sono sempre disallineati fra loro: il
/// server deve poter rifiutare esplicitamente una versione incompatibile invece
/// di fallire in modo oscuro (spec §4.1).
/// </summary>
public static class ProtocolVersion
{
    public const int Current = 1;

    public static bool IsCompatible(int clientVersion) => clientVersion == Current;
}
