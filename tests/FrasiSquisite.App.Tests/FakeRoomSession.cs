using FrasiSquisite.App.Services;

namespace FrasiSquisite.App.Tests;

public sealed class FakeRoomSession : IRoomSession
{
    public string RoomCode { get; private set; } = string.Empty;

    public List<string> Salvati { get; } = [];

    public bool Cancellato { get; private set; }

    public void Save(string roomCode)
    {
        RoomCode = roomCode;
        Salvati.Add(roomCode);
    }

    public void Clear()
    {
        RoomCode = string.Empty;
        Cancellato = true;
    }
}
