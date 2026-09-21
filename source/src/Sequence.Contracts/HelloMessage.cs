namespace Sequence.Contracts;

/// <summary>
/// The first frame a client sends after the TCP connection opens. It claims a player
/// identity, which the server uses as the seat key: reconnecting with the same identity
/// returns the same seat, so a client can resume after a temporary drop. The identity is
/// not an authenticated account - it is a 2-player conveniency and is shown to the
/// opponent so they know who left.
/// </summary>
public sealed record HelloMessage(string ClientId);