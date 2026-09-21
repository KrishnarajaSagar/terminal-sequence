using System.Text.Json.Serialization;
using Sequence.Domain;

namespace Sequence.Contracts;

/// <summary>
/// A message sent from the server to a client. It always carries a player-specific
/// <see cref="PlayerView"/>; the acting player's seat fills in which client receives it.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GameStartedMessage), "gameStarted")]
[JsonDerivedType(typeof(GameStateUpdatedMessage), "gameStateUpdated")]
[JsonDerivedType(typeof(ActionRejectedMessage), "actionRejected")]
[JsonDerivedType(typeof(PlayerDisconnectedMessage), "playerDisconnected")]
public abstract record ServerMessage;

/// <summary>Sent to a client when it first connects, describing the game as it stands.</summary>
public sealed record GameStartedMessage(PlayerView View) : ServerMessage;

/// <summary>Sent after a successful action: the receiver's updated view plus the events produced.</summary>
public sealed record GameStateUpdatedMessage(PlayerView View, IReadOnlyList<GameEvent> Events) : ServerMessage;

/// <summary>Sent when an action was rejected. No state changed.</summary>
public sealed record ActionRejectedMessage(ActionError Error) : ServerMessage;

/// <summary>
/// Sent to the remaining client(s) when a player disconnects or reconnects. The game can
/// be resumed when that identity reconnects.
/// </summary>
public sealed record PlayerDisconnectedMessage(PlayerId PlayerId, string PlayerName) : ServerMessage;