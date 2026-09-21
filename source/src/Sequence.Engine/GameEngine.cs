using System.Collections.Immutable;
using Sequence.Domain;

namespace Sequence.Engine;

/// <summary>
/// Authoritative engine: it validates every submitted action and applies it to the
/// immutable <see cref="GameState"/>. The engine never depends on a console, network, or UI.
/// </summary>
public static class GameEngine
{
    private static readonly BoardLayout StandardBoard = BoardLayout.Standard;

    /// <summary>Deals a new game: shuffles the deck, decides the first player, deals 7 cards each.</summary>
    public static GameState StartGame(GameSetup setup)
    {
        if (setup.SequenceTarget < 1)
        {
            throw new ArgumentException("SequenceTarget must be at least 1.", nameof(setup));
        }

        EnsureName(setup.FirstPlayerName);
        EnsureName(setup.SecondPlayerName);

        Card[] shuffled = DeckShuffler.Shuffle(Deck.CreateStandard(), new SeededRandom(setup.DeckSeed));

        // The opening draw decides who starts. Each player keeps the card they drew and
        // receives six more cards, one at a time, alternately.
        PlayerId firstPlayer = CompareForFirst(shuffled[0], shuffled[1])
            ? new PlayerId(0)
            : new PlayerId(1);

        PlayerInfo first = new(new PlayerId(0), setup.FirstPlayerName, setup.FirstPlayerColor, HandOf(shuffled, 0));
        PlayerInfo second = new(new PlayerId(1), setup.SecondPlayerName, setup.SecondPlayerColor, HandOf(shuffled, 1));

        ImmutableQueue<Card> drawPile = ImmutableQueue<Card>.Empty;
        for (int i = GameSetup.MinHandSize * 2; i < Deck.TotalCards; i++)
        {
            drawPile = drawPile.Enqueue(shuffled[i]);
        }

        ImmutableDictionary<PlayerId, int> counts = ImmutableDictionary<PlayerId, int>.Empty
            .Add(new PlayerId(0), 0)
            .Add(new PlayerId(1), 0);

        return new GameState(
            Setup: setup,
            Board: BoardState.Empty,
            Players: ImmutableList.Create(first, second),
            DrawPile: drawPile,
            DiscardPile: ImmutableList<Card>.Empty,
            CurrentPlayerId: firstPlayer,
            TurnNumber: 1,
            CurrentPlayerHasExchangedDeadCard: false,
            CompletedSequences: ImmutableList<CompletedSequence>.Empty,
            SequenceCounts: counts,
            ShuffleRound: 0,
            Phase: TurnPhase.Playing);
    }

    /// <summary>True when the acting player may exchange <paramref name="card"/> this turn.</summary>
    public static bool CanExchangeDeadCard(GameState state, PlayerId playerId, Card card) =>
        state.Status == GameStatus.InProgress
        && playerId == state.CurrentPlayerId
        && !state.CurrentPlayerHasExchangedDeadCard
        && IsDeadCard(state, playerId, card);

    /// <summary>A card is dead when its two matching board spaces are both occupied.</summary>
    public static bool IsDeadCard(GameState state, PlayerId playerId, Card card) =>
        !card.IsJack
        && state.GetPlayer(playerId).Hand.Contains(card)
        && BoardLayout.Standard.GetPositionsForCard(card).All(position => state.Board.IsOccupied(position));

    /// <summary>Validates and applies a single action.</summary>
    public static ActionResult ApplyAction(GameState state, PlayerAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);

        if (state.Status != GameStatus.InProgress)
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.GameOver, "The game has ended. No further actions are accepted."));
        }

        if (action.PlayerId != state.CurrentPlayerId)
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.NotYourTurn, "It is not this player's turn.", action.PlayerId));
        }

        return action switch
        {
            PlayCardAction play => PlayCard(state, play),
            PlayTwoEyedJackAction play => PlayTwoEyedJack(state, play),
            PlayOneEyedJackAction play => PlayOneEyedJack(state, play),
            ExchangeDeadCardAction exchange => ExchangeDeadCard(state, exchange),
            _ => ActionResult.Failure(new ActionError(ActionErrorCode.WrongActionForCard, "Unknown action type.", action.PlayerId)),
        };
    }

    // ------------------------------------------------------------------ normal move

    private static ActionResult PlayCard(GameState state, PlayCardAction action)
    {
        PlayerInfo player = state.GetPlayer(action.PlayerId);

        if (action.Card.IsJack)
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.WrongActionForCard, "Jacks are played with a jack action.", action.PlayerId));
        }

        if (!player.Hand.Contains(action.Card))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.CardNotInHand, $"{action.Card.Code} is not in the hand.", action.PlayerId));
        }

        IReadOnlyList<BoardPosition> matches = StandardBoard.GetPositionsForCard(action.Card);
        if (!matches.Contains(action.Target))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.TargetDoesNotMatchCard, $"{action.Card.Code} does not appear at {action.Target}.", action.PlayerId));
        }

        if (state.Board.IsOccupied(action.Target))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.TargetOccupied, $"{action.Target} already holds a chip. If both matching spaces are occupied, exchange the card instead.", action.PlayerId));
        }

        BoardState board = state.Board.PlaceChip(action.Target, action.PlayerId);

        GameState mid = state.WithBoard(board);
        var events = new List<GameEvent>
        {
            new CardPlayedEvent(action.PlayerId, action.Card),
            new ChipPlacedEvent(action.PlayerId, action.Target),
        };

        return ResolvePlacement(mid, action.Target, action.PlayerId, action.Card, events);
    }

    // ------------------------------------------------------------------ jack moves

    private static ActionResult PlayTwoEyedJack(GameState state, PlayTwoEyedJackAction action)
    {
        PlayerInfo player = state.GetPlayer(action.PlayerId);

        if (!action.Card.IsTwoEyedJack)
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.WrongActionForCard, "This card is not a two-eyed Jack.", action.PlayerId));
        }

        if (!player.Hand.Contains(action.Card))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.CardNotInHand, $"{action.Card.Code} is not in the hand.", action.PlayerId));
        }

        if (!action.Target.IsInsideBoard)
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.TargetOutsideBoard, $"{action.Target} is outside the board.", action.PlayerId));
        }

        if (StandardBoard.IsFreeCorner(action.Target))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.TargetIsFreeCorner, "A chip cannot be placed on a FREE corner.", action.PlayerId));
        }

        if (state.Board.IsOccupied(action.Target))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.TargetOccupied, $"{action.Target} already holds a chip.", action.PlayerId));
        }

        BoardState board = state.Board.PlaceChip(action.Target, action.PlayerId);

        GameState mid = state.WithBoard(board);
        var events = new List<GameEvent>
        {
            new CardPlayedEvent(action.PlayerId, action.Card),
            new ChipPlacedEvent(action.PlayerId, action.Target),
        };

        return ResolvePlacement(mid, action.Target, action.PlayerId, action.Card, events);
    }

    private static ActionResult PlayOneEyedJack(GameState state, PlayOneEyedJackAction action)
    {
        PlayerInfo player = state.GetPlayer(action.PlayerId);

        if (!action.Card.IsOneEyedJack)
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.WrongActionForCard, "This is not a one-eyed Jack.", action.PlayerId));
        }

        if (!player.Hand.Contains(action.Card))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.CardNotInHand, $"{action.Card.Code} is not in the hand.", action.PlayerId));
        }

        if (!action.Target.IsInsideBoard)
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.TargetOutsideBoard, $"{action.Target} is outside the board.", action.PlayerId));
        }

        if (!state.Board.IsOccupied(action.Target))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.TargetNotOccupied, "A one-eyed Jack removes an opponent's chip; there is no chip here.", action.PlayerId));
        }

        if (state.Board.IsLocked(action.Target))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.CannotRemoveLockedChip, "A chip of a completed Sequence cannot be removed.", action.PlayerId));
        }

        PlayerId removedPlayer = state.Board.Chips[action.Target];
        if (removedPlayer == action.PlayerId)
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.CannotRemoveOwnChip, "A player cannot remove their own chip.", action.PlayerId));
        }

        BoardState board = state.Board.RemoveChip(action.Target);

        GameState mid = state.WithBoard(board);
        var events = new List<GameEvent>
        {
            new CardPlayedEvent(action.PlayerId, action.Card),
            new ChipRemovedEvent(action.PlayerId, action.Target, removedPlayer),
        };

        GameState afterDraw = DrawReplacement(mid, action.PlayerId, action.Card, events);
        return FinishTurnAfterDraw(afterDraw, action.PlayerId, events);
    }

    // ------------------------------------------------------------------ dead-card exchange

    private static ActionResult ExchangeDeadCard(GameState state, ExchangeDeadCardAction action)
    {
        PlayerInfo player = state.GetPlayer(action.PlayerId);

        if (!player.Hand.Contains(action.Card))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.CardNotInHand, $"{action.Card.Code} is not in the hand.", action.PlayerId));
        }

        if (state.CurrentPlayerHasExchangedDeadCard)
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.AlreadyExchangedThisTurn, "Only one dead card may be exchanged per turn.", action.PlayerId));
        }

        if (!IsDeadCard(state, action.PlayerId, action.Card))
        {
            return ActionResult.Failure(new ActionError(ActionErrorCode.NotADeadCard, "The card still has a legal matching space and cannot be exchanged.", action.PlayerId));
        }

        DeckManager.DrawOutcome draw = DeckManager.Draw(state.DrawPile, state.DiscardPile, state.ShuffleRound, state.Setup.DeckSeed);
        GameState afterDraw = state
            .WithPlayerHand(action.PlayerId, player.Hand.Remove(action.Card).Add(draw.Drawn))
            .WithDrawPile(draw.DrawPile)
            .WithDiscardPile(draw.DiscardPile.Add(action.Card)) with
        {
            CurrentPlayerHasExchangedDeadCard = true,
            ShuffleRound = draw.NextShuffleRound,
        };

        var events = new List<GameEvent>();
        if (draw.WasReshuffled)
        {
            events.Add(new DeckReshuffledEvent(draw.ReshuffledCardCount));
        }

        events.Add(new DeadCardExchangedEvent(action.PlayerId, action.Card, draw.Drawn));
        events.Add(new CardDrawnEvent(action.PlayerId, draw.Drawn));

        return ActionResult.Success(afterDraw, events);
    }

    // ------------------------------------------------------------------ shared helpers

    /// <summary>
    /// After a chip placement: detects and locks completed Sequences, draws the
    /// replacement card, then either ends the turn or (on reaching the target) the game.
    /// </summary>
    private static ActionResult ResolvePlacement(GameState state, BoardPosition placed, PlayerId player, Card played, List<GameEvent> events)
    {
        IReadOnlyList<ImmutableArray<BoardPosition>> sequences = SequenceDetector.FindNewlyCompleted(state.Board, StandardBoard, placed, player);

        GameState afterScoring = state;
        foreach (ImmutableArray<BoardPosition> window in sequences)
        {
            afterScoring = afterScoring with
            {
                Board = afterScoring.Board.LockPositions(window),
                CompletedSequences = afterScoring.CompletedSequences.Add(CompletedSequence.Create(player, window)),
                SequenceCounts = afterScoring.SequenceCounts.SetItem(player, afterScoring.SequenceCount(player) + 1),
            };

            events.Add(new SequenceCompletedEvent(player, window));
        }

        GameState afterDraw = DrawReplacement(afterScoring, player, played, events);
        return FinishTurnAfterDraw(afterDraw, player, events);
    }

    /// <summary>Draws the replacement card, removes the played (or exchanged) card from
    /// the hand, and routes the played card to the shared discard pile.</summary>
    private static GameState DrawReplacement(GameState state, PlayerId player, Card played, List<GameEvent> events)
    {
        DeckManager.DrawOutcome draw = DeckManager.Draw(state.DrawPile, state.DiscardPile, state.ShuffleRound, state.Setup.DeckSeed);
        GameState after = state
            .WithPlayerHand(player, state.GetPlayer(player).Hand.Remove(played).Add(draw.Drawn))
            .WithDrawPile(draw.DrawPile)
            .WithDiscardPile(draw.DiscardPile.Add(played)) with
        {
            ShuffleRound = draw.NextShuffleRound,
        };

        if (draw.WasReshuffled)
        {
            events.Add(new DeckReshuffledEvent(draw.ReshuffledCardCount));
        }

        events.Add(new CardDrawnEvent(player, draw.Drawn));
        return after;
    }

    /// <summary>Ends the turn (or the game, if the player reached the target).</summary>
    private static ActionResult FinishTurnAfterDraw(GameState state, PlayerId player, List<GameEvent> events)
    {
        if (state.HasReachedSequenceTarget(player))
        {
            GameState done = state with { Phase = TurnPhase.GameOver };
            events.Add(new GameWonEvent(player));
            return ActionResult.Success(done, events);
        }

        PlayerId next = state.Players.Single(p => p.Id != player).Id;
        GameState advanced = state with
        {
            CurrentPlayerId = next,
            TurnNumber = state.TurnNumber + 1,
            CurrentPlayerHasExchangedDeadCard = false,
        };

        events.Add(new TurnChangedEvent(next, advanced.TurnNumber));
        return ActionResult.Success(advanced, events);
    }

    /// <summary>One player's 7-card hand: the opening card plus six alternating cards.</summary>
    private static ImmutableList<Card> HandOf(Card[] deck, int playerIndex)
    {
        ImmutableList<Card> hand = ImmutableList<Card>.Empty;

        for (int deal = 0; deal < GameSetup.MinHandSize; deal++)
        {
            hand = hand.Add(deck[deal * GameSetup.MinPlayers + playerIndex]);
        }

        return hand;
    }

    /// <summary>True when the first-named player draws the higher card and starts.</summary>
    private static bool CompareForFirst(Card a, Card b) => a.Rank != b.Rank ? a.Rank > b.Rank : a.Suit > b.Suit;

    private static void EnsureName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A player name is required.", nameof(name));
        }
    }
}