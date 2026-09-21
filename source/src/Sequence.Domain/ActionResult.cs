namespace Sequence.Domain;

/// <summary>
/// Outcome of applying a <see cref="PlayerAction"/>. A rejected action carries an
/// explicit <see cref="ActionError"/> and never produces a new state.
/// </summary>
public sealed record ActionResult(
    bool IsSuccess,
    GameState? NewState,
    IReadOnlyList<GameEvent> Events,
    ActionError? Error)
{
    public static ActionResult Success(GameState state, IReadOnlyList<GameEvent> events) =>
        new(true, state, events, null);

    public static ActionResult Failure(ActionError error) =>
        new(false, null, Array.Empty<GameEvent>(), error);
}