namespace SshTunneler.Models;

/// <summary>Immutable snapshot of a tunnel state, published by the tunnel manager.</summary>
public sealed record TunnelStatus(
    Guid ResourceId,
    TunnelState State,
    int LocalPort,
    string? Message,
    DateTime ChangedAt)
{
    public static TunnelStatus Idle(Guid resourceId)
        => new(resourceId, TunnelState.Idle, 0, null, DateTime.Now);
}
