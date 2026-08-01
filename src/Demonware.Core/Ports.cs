namespace Demonware.Core;

/// <summary>
/// Well-known listen endpoints.
/// STUN (UDP) and Modern lobby (TCP) share port 3074 by design.
/// </summary>
public static class Ports
{
    public const int StunUdp = 3074;
    public const int ModernTcp = 3074;
    public const int LegacyTcp = 3078;
    public const int GatewayHttp = 80;
    public const int GatewayHttpFallback = 8080;
}

public enum TitleId : uint
{
    T5 = 18301,
    Iw5 = 18409,
    Iw6 = 0x415608C0
}
