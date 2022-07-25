namespace Stride.Graphics.Direct3D;

public enum DXGIError : long
{
    InvalidCall = 0x887A0001,
    NotFound = 0x887A0002,
    MoreData = 0x887A0003,
    Unsupported = 0x887A0004,
    DeviceRemoved = 0x887A0005,
    DeviceHung = 0x887A0006,
    DeviceReset = 0x887A0007,
    WasStillDrawing = 0x887A000A,
    FrameStatisticsDisjoint = 0x887A000B,
    GraphicsVIDPNSourceInUse = 0x887A000C,
    DriverInternalError = 0x887A0020,
    NonExclusive = 0x887A0021,
    NotCurrentlyAvailable = 0x887A0022,
    RemoteClientDisconnected = 0x887A0023,
    RemoteOutOfMemory = 0x887A0024,
    AccessLost = 0x887A0026,
    WaitTimeout = 0x887A0027,
    SessionDisconnected = 0x887A0028,
    RestrictToOutputStale = 0x887A0029,
    CannotProtectContent = 0x887A002A,
    AccessDenied = 0x887A002B
}

