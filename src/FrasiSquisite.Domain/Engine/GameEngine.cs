using FrasiSquisite.Domain.Filling;
using FrasiSquisite.Domain.Model;
using FrasiSquisite.Domain.Modes;
using FrasiSquisite.Domain.Randomness;
using FrasiSquisite.Shared.Protocol;
using FrasiSquisite.Shared.Validation;

namespace FrasiSquisite.Domain.Engine;

public sealed partial class GameEngine(
    IGameMode mode,
    IWordPool pool,
    IRandomSource random,
    int massimoIllustrazioniPerStanza = int.MaxValue) : IGameEngine
{
    public const int MinPlayers = 2;

    /// <summary>
    /// Dal design: il pulsante "aggiungi bot" scompare a 9 giocatori
    /// (lotto-b-brief.md).
    /// </summary>
    public const int MaxPlayers = 9;

    /// <summary>
    /// Nomi assegnati ai bot, in ordine, deterministicamente: primo libero
    /// della lista, mai casuale (lotto-b-brief.md, punto 2). Otto voci perché
    /// con <see cref="MaxPlayers"/> a 9 e almeno un host umano, otto bot sono
    /// il massimo possibile in una stanza.
    /// </summary>
    private static readonly string[] BotNames =
        ["Bot Ada", "Bot Bruno", "Bot Chiara", "Bot Delia", "Bot Enzo", "Bot Fiamma", "Bot Gigi", "Bot Ivo"];

    private readonly IGameMode _mode = mode;
    private readonly IWordPool _pool = pool;
    private readonly IRandomSource _random = random;
    private readonly int _massimoIllustrazioniPerStanza = massimoIllustrazioniPerStanza;

    public EngineResult Handle(GameState state, GameEvent evt) => evt switch
    {
        PlayerJoined e => OnPlayerJoined(state, e),
        PlayerLeft e => OnPlayerLeft(state, e),
        PlayerRejoined e => OnPlayerRejoined(state, e),
        GameStartRequested e => OnGameStartRequested(state, e),
        SlotSubmitted e => OnSlotSubmitted(state, e),
        RevealAdvanceRequested e => OnRevealAdvance(state, e),
        NewGameRequested e => OnNewGameRequested(state, e),
        BackToLobbyRequested e => OnBackToLobbyRequested(state, e),
        BotAdded e => OnBotAdded(state, e),
        BotRemoved e => OnBotRemoved(state, e),
        BotRenamed e => OnBotRenamed(state, e),
        SchemaSelected e => OnSchemaSelected(state, e),
        VoteCast e => OnVoteCast(state, e),
        VotingCloseRequested e => OnVotingCloseRequested(state, e),
        RefinementFinished e => OnRefinementFinished(state, e),
        IllustrationRequested e => OnIllustrationRequested(state, e),
        IllustrationFinished e => OnIllustrationFinished(state, e),
        _ => EngineResult.NoChange(state),
    };

    /// <summary>
    /// Il connesso presente da più tempo tra i giocatori passati, o
    /// Guid.Empty se non ne resta nessuno: stessa regola di successione
    /// dell'host usata sia quando l'host abbandona la stanza
    /// (<see cref="OnPlayerLeft"/>) sia quando l'espulsione dei disconnessi
    /// al reset lo porta via (<see cref="AzzeraPerNuovaPartita"/>).
    /// </summary>
    private static Guid HostPiuAnzianoTraIConnessi(IEnumerable<Player> giocatori) =>
        giocatori.Where(p => p.IsConnected).OrderBy(p => p.JoinOrder).FirstOrDefault()?.Id ?? Guid.Empty;

    private static EngineResult Error(GameState state, Guid playerId, string code, string message) =>
        new(state, [new SendToPlayer(playerId, new ErrorMessage(code, message))]);

    private static RoomStateMessage RoomState(GameState state) =>
        new(
            state.RoomCode,
            state.Phase.ToString(),
            [.. state.Players.Select(p => new PlayerView(p.Id, p.Nickname, p.Id == state.HostId, p.IsConnected, p.IsBot))],
            state.Schema.Id,
            state.Schema.SlotCount,
            state.AvailableSchemas);
}
