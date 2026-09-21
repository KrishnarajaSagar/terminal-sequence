using System.Collections.Immutable;
using Sequence.Domain;
using Sequence.Engine;
using Sequence.Server;

namespace Sequence.Tests;

/// <summary>
/// Step 9: the server can persist the authoritative <see cref="GameState"/> to JSON and
/// restore it, then continue the game exactly where it left off (server restart).
/// </summary>
public class PersistenceTests
{
    private static GameServer NewServer() =>
        new(new GameSetup("Alice", "Bob", 4242, 2, PlayerColor.Green, PlayerColor.Blue));

    private static string TempSaveDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "seq-persistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Plays one legal move for whoever's turn it is, using the client seated on that seat.</summary>
    private static bool PlayOneLegalMove(GameServer server)
    {
        PlayerId current = server.CurrentPlayerId;
        string acting = server.SeatForClient("a") == current ? "a" : "b";
        PlayerView view = server.GetPlayerView(current);
        Card playable = view.Hand.First(c => !c.IsJack
            && BoardLayout.Standard.GetPositionsForCard(c).Any(p => !view.Board.IsOccupied(p) && !view.Board.IsLocked(p)));
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(playable)
            .First(p => !view.Board.IsOccupied(p) && !view.Board.IsLocked(p));

        ActionResult result = server.SubmitAction(acting, new PlayCardAction(current, playable, target));
        Assert.True(result.IsSuccess, result.Error?.ToString());
        return true;
    }

    [Fact]
    public void Save_Then_Load_RoundTrips_The_Authoritative_State()
    {
        string dir = TempSaveDirectory();
        try
        {
            var original = NewServer();
            _ = original.ConnectPlayer("a");
            _ = original.ConnectPlayer("b");
            PlayOneLegalMove(original);
            PlayOneLegalMove(original); // second call acts for the other client's seat

            GameState before = original.CurrentState;

            Assert.False(GameStateStore.Exists("test", dir));
            GameStateStore.Save(before, "test", dir);
            Assert.True(GameStateStore.Exists("test", dir));
            GameState loaded = GameStateStore.Load("test", dir);

            Assert.Equal(before.Setup, loaded.Setup);
            Assert.Equal(before.TurnNumber, loaded.TurnNumber);
            Assert.Equal(before.CurrentPlayerId, loaded.CurrentPlayerId);
            Assert.Equal(before.CurrentPlayerHasExchangedDeadCard, loaded.CurrentPlayerHasExchangedDeadCard);
            Assert.Equal(before.ShuffleRound, loaded.ShuffleRound);
            Assert.Equal(before.Phase, loaded.Phase);
            Assert.Equal(before.Status, loaded.Status);

            Assert.Equal(before.Players.Count, loaded.Players.Count);
            for (int i = 0; i < before.Players.Count; i++)
            {
                Assert.Equal(before.Players[i].Id, loaded.Players[i].Id);
                Assert.Equal(before.Players[i].Name, loaded.Players[i].Name);
                Assert.Equal(before.Players[i].Color, loaded.Players[i].Color);
                Assert.Equal(before.Players[i].Hand, loaded.Players[i].Hand);
            }

            // The draw pile is the tricky immutable queue: order must survive, including
            // when a reshuffle has already re-ordered the deck mid-game.
            Assert.Equal(before.DrawPile.Count(), loaded.DrawPile.Count());
            Assert.True(before.DrawPile.SequenceEqual(loaded.DrawPile));
            Assert.Equal(before.DiscardPile, loaded.DiscardPile);

            foreach ((BoardPosition position, PlayerId owner) in before.Board.Chips)
            {
                Assert.Equal(owner, loaded.Board.ChipsAt(position));
            }

            Assert.Equal(before.Board.Chips.Count, loaded.Board.Chips.Count);
            Assert.Equal(before.Board.LockedPositions.Count, loaded.Board.LockedPositions.Count);
            Assert.Equal(before.CompletedSequences.Count, loaded.CompletedSequences.Count);
            Assert.Equal(before.SequenceCount(new PlayerId(0)), loaded.SequenceCount(new PlayerId(0)));
            Assert.Equal(before.SequenceCount(new PlayerId(1)), loaded.SequenceCount(new PlayerId(1)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Restored_Server_Continues_The_Game_Seamlessly()
    {
        string dir = TempSaveDirectory();
        try
        {
            var original = NewServer();
            _ = original.ConnectPlayer("a");
            _ = original.ConnectPlayer("b");
            PlayOneLegalMove(original);

            string path = GameStateStore.Save(original.CurrentState, "restore", dir);

            // Simulate a restart: a brand-new server built from the save, no memory left.
            var restored = new GameServer(GameStateStore.Load("restore", dir));
            PlayerId current = restored.CurrentPlayerId;
            Assert.Equal(original.CurrentPlayerId, current);
            Assert.Equal(original.CurrentState.TurnNumber, restored.CurrentState.TurnNumber);

            _ = restored.ConnectPlayer("a");
            _ = restored.ConnectPlayer("b");

            // The player whose turn it is can still act...
            string acting = restored.SeatForClient("a") == current ? "a" : "b";
            PlayerView view = restored.GetPlayerView(current);
            Card playable = view.Hand.First(c => !c.IsJack
                && BoardLayout.Standard.GetPositionsForCard(c).Any(p => !view.Board.IsOccupied(p) && !view.Board.IsLocked(p)));
            BoardPosition target = BoardLayout.Standard.GetPositionsForCard(playable)
                .First(p => !view.Board.IsOccupied(p) && !view.Board.IsLocked(p));

            ActionResult next = restored.SubmitAction(acting, new PlayCardAction(current, playable, target));
            Assert.True(next.IsSuccess, next.Error?.ToString());
            Assert.NotEqual(current, restored.CurrentPlayerId);
            Assert.Contains(next.Events, e => e is TurnChangedEvent);

            // ...and a client submitting an action for the seat that just moved is refused:
            // the turn has moved on, so that seat acting again is out of turn.
            PlayerView justMovedView = restored.GetPlayerView(current);
            Card againCard = justMovedView.Hand.First(c => !c.IsJack
                && BoardLayout.Standard.GetPositionsForCard(c).Any(p => !justMovedView.Board.IsOccupied(p) && !justMovedView.Board.IsLocked(p)));
            ActionResult outOfTurn = restored.SubmitAction(
                acting,
                new PlayCardAction(
                    current,
                    againCard,
                    BoardLayout.Standard.GetPositionsForCard(againCard).First(p => !justMovedView.Board.IsOccupied(p) && !justMovedView.Board.IsLocked(p))));

            Assert.False(outOfTurn.IsSuccess);
            Assert.Equal(ActionErrorCode.NotYourTurn, outOfTurn.Error!.Code);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Restored_State_Still_Enforces_Information_Visibility()
    {
        string dir = TempSaveDirectory();
        try
        {
            var original = NewServer();
            PlayerId p0Seat = original.ConnectPlayer("a");
            _ = original.ConnectPlayer("b");
            PlayOneLegalMove(original);

            GameStateStore.Save(original.CurrentState, "priv", dir);
            var restored = new GameServer(GameStateStore.Load("priv", dir));

            PlayerView p0 = restored.GetPlayerView(p0Seat);
            PlayerView p1 = restored.GetPlayerView(new PlayerId(1));

            Assert.Equal(7, p0.Hand.Count);
            Assert.Equal(7, p1.Hand.Count);
            Assert.NotEqual(p0.Hand, p1.Hand);
            Assert.DoesNotContain(typeof(PlayerSummary).GetProperties(), prop => prop.Name == "Hand");
            Assert.DoesNotContain(typeof(PlayerView).GetProperties(), prop => prop.Name == "DrawPile");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_Names_Resolve_To_Files_And_Reject_Path_Escape()
    {
        string name = GameStateStore.ResolvePath("demo");
        Assert.EndsWith("demo.json", name, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("game.json", Path.GetFileName(GameStateStore.ResolvePath("game.json")));

        Assert.Throws<ArgumentException>(() => GameStateStore.ResolvePath("..\\other"));
        Assert.Throws<ArgumentException>(() => GameStateStore.ResolvePath("saves\\other"));
    }

    [Fact]
    public void Loading_A_Missing_Save_Throws()
    {
        string dir = TempSaveDirectory();
        try
        {
            Assert.Throws<FileNotFoundException>(() => GameStateStore.Load("nope", dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_File_Can_Be_Overwritten_Atomically()
    {
        string dir = TempSaveDirectory();
        try
        {
            string path = GameStateStore.Save(NewServer().CurrentState, "rw", dir);
            long firstLength = new FileInfo(path).Length;

            var secondServer = NewServer();
            _ = secondServer.ConnectPlayer("a");
            _ = secondServer.ConnectPlayer("b");
            PlayOneLegalMove(secondServer);

            string rewritten = GameStateStore.Save(secondServer.CurrentState, "rw", dir);
            Assert.Equal(path, rewritten);
            Assert.True(new FileInfo(path).Length > 0);
            Assert.False(File.Exists(path + ".tmp"));

            GameState reloaded = GameStateStore.Load("rw", dir);
            Assert.Equal(secondServer.CurrentState.TurnNumber, reloaded.TurnNumber);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}