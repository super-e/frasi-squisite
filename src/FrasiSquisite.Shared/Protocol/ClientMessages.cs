namespace FrasiSquisite.Shared.Protocol;

public sealed record CreateRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname);

public sealed record JoinRoomRequest(int ProtocolVersion, Guid PlayerId, string Nickname, string RoomCode);

public sealed record StartGameRequest(string RoomCode);

public sealed record SubmitSlotRequest(string RoomCode, string Text);
