namespace FrasiSquisite.Shared.Protocol;

public sealed record CreateRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname);

public sealed record JoinRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname, string RoomCode);

public sealed record StartGameRequest(string RoomCode);

public sealed record SubmitSlotRequest(string RoomCode, string Text);

public sealed record AddBotRequest(string RoomCode);

public sealed record RemoveBotRequest(string RoomCode, Guid BotId);

public sealed record RenameBotRequest(string RoomCode, Guid BotId, string Nickname);

public sealed record SetSchemaRequest(string RoomCode, string SchemaId);

public sealed record NewGameRequest(string RoomCode);

public sealed record BackToLobbyRequest(string RoomCode);
