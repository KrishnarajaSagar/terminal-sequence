using System.Net;
using Sequence.Terminal;
using Sequence.Domain;
using Spectre.Console;

namespace Sequence.Tests;

/// <summary>
/// Covers the keyboard-driven menu screens. Input is scripted through <see cref="IMenuInput"/>
/// and rendering is captured through an <see cref="IAnsiConsole"/> writing to a StringWriter, so
/// no live console is touched and the screens can be verified deterministically.
/// </summary>
public class MenuTests
{
    // ----- helpers -----

    private static ConsoleKeyInfo Enter => new('\r', ConsoleKey.Enter, false, false, false);

    private static ConsoleKeyInfo Escape => new('\x1b', ConsoleKey.Escape, false, false, false);

    private static ConsoleKeyInfo Backspace => new('\b', ConsoleKey.Backspace, false, false, false);

    private static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.A, false, false, false);

    private static ConsoleKeyInfo MoveKey(ConsoleKey key) => new('\0', key, false, false, false);

    private static ConsoleKeyInfo Down => MoveKey(ConsoleKey.DownArrow);

    private static ConsoleKeyInfo Up => MoveKey(ConsoleKey.UpArrow);

    private static ConsoleKeyInfo Tab => MoveKey(ConsoleKey.Tab);

    private static IMenuInput Keys(params ConsoleKeyInfo[] keys) => new KeyQueueInput(keys);

    private static IAnsiConsole Spool(out StringWriter writer)
    {
        writer = new StringWriter();
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            Out = new AnsiConsoleOutput(writer),
        });
    }

    private sealed class KeyQueueInput : IMenuInput
    {
        private readonly Queue<ConsoleKeyInfo> _keys;

        public KeyQueueInput(IEnumerable<ConsoleKeyInfo> keys) => _keys = new Queue<ConsoleKeyInfo>(keys);

        public ConsoleKeyInfo ReadKey() =>
            _keys.Count == 0
                ? throw new InvalidOperationException("Script ran out of keys before the screen finished.")
                : _keys.Dequeue();
    }

    private static MutableField Field(
        string label,
        string value,
        bool numeric = false,
        IReadOnlyList<string>? choices = null) =>
        new()
        {
            Label = label,
            Value = value,
            Placeholder = string.Empty,
            Numeric = numeric,
            Choices = choices,
        };

    // ----- MainMenu -----

    [Fact]
    public void MainMenu_Shows_Title_And_Options()
    {
        IAnsiConsole console = Spool(out StringWriter writer);
        MainMenuChoice choice = MainMenu.Show(Keys(Char('3'), Enter), console);

        Assert.Equal(MainMenuChoice.Exit, choice);
        Assert.Contains("SEQUENCE", writer.ToString());
        Assert.Contains("Join Game", writer.ToString());
        Assert.Contains("Host Game", writer.ToString());
    }

    [Fact]
    public void MainMenu_Enter_Defaults_To_Host() =>
        Assert.Equal(MainMenuChoice.Host, MainMenu.Show(Keys(Enter), Spool(out _)));

    [Fact]
    public void MainMenu_Down_Then_Enter_Joins() =>
        Assert.Equal(MainMenuChoice.Join, MainMenu.Show(Keys(Down, Enter), Spool(out _)));

    [Fact]
    public void MainMenu_Up_Wraps_To_Last_Option() =>
        Assert.Equal(MainMenuChoice.Exit, MainMenu.Show(Keys(Up, Enter), Spool(out _)));

    [Fact]
    public void MainMenu_Number_Keys_Select_Then_Enter_Confirms() =>
        Assert.Equal(MainMenuChoice.Join, MainMenu.Show(Keys(Char('2'), Enter), Spool(out _)));

    [Fact]
    public void MainMenu_Number_Key_Out_Of_Range_Is_Ignored() =>
        Assert.Equal(MainMenuChoice.Host, MainMenu.Show(Keys(Char('9'), Enter), Spool(out _)));

    // ----- MenuNavigation -----

    [Theory]
    [InlineData(0, 3, 2)]
    [InlineData(1, 3, 0)]
    [InlineData(2, 3, 1)]
    public void Move_Up_Decrements_And_Wraps(int selected, int count, int expected)
    {
        MenuNavigation.Move(Up, count, ref selected);
        Assert.Equal(expected, selected);
    }

    [Theory]
    [InlineData(2, 3, 0)]
    [InlineData(0, 3, 1)]
    [InlineData(1, 3, 2)]
    public void Move_Down_Increments_And_Wraps(int selected, int count, int expected)
    {
        MenuNavigation.Move(Down, count, ref selected);
        Assert.Equal(expected, selected);
    }

    [Fact]
    public void Move_Tab_Acts_As_Down()
    {
        int selected = 0;
        MenuNavigation.Move(Tab, 3, ref selected);
        Assert.Equal(1, selected);
    }

    [Theory]
    [InlineData('1', 0)]
    [InlineData('9', 8)]
    public void SelectNumber_Maps_Digits_To_Zero_Based_Indexes(char digit, int expected)
    {
        Assert.True(MenuNavigation.SelectNumber(Char(digit), 9, out int index));
        Assert.Equal(expected, index);
    }

    [Fact]
    public void SelectNumber_Rejects_Digits_Beyond_Count_And_Non_Digits()
    {
        Assert.False(MenuNavigation.SelectNumber(Char('5'), 3, out _));
        Assert.False(MenuNavigation.SelectNumber(Char('0'), 3, out _));
        Assert.False(MenuNavigation.SelectNumber(Char('a'), 3, out _));
    }

    // ----- FormEdit -----

    [Fact]
    public void FormEdit_Append_Filters_Digits_For_Numeric_Fields()
    {
        Assert.Equal("500", FormEdit.Append("50", '0', numericOnly: true));
        Assert.Equal("50", FormEdit.Append("50", 'a', numericOnly: true));
        Assert.Equal("50a", FormEdit.Append("50", 'a', numericOnly: false));
    }

    [Fact]
    public void FormEdit_Backspace_Drops_The_Last_Character()
    {
        Assert.Equal("12", FormEdit.Backspace("123"));
        Assert.Equal(string.Empty, FormEdit.Backspace("1"));
        Assert.Equal(string.Empty, FormEdit.Backspace(string.Empty));
    }

    // ----- FormScreen.Cycle -----

    [Theory]
    [InlineData(true, "Auto", "Green")]
    [InlineData(true, "Cyan", "Auto")] // forward wraps
    [InlineData(false, "Auto", "Cyan")] // backward wraps
    public void Cycle_Moves_Forward_And_Backward_Through_Choices(bool forward, string current, string expected)
    {
        string[] choices = { "Auto", "Green", "Blue", "Yellow", "Magenta", "Cyan" };
        Assert.Equal(expected, FormScreen.Cycle(choices, current, forward));
    }

    [Fact]
    public void Cycle_Snaps_An_Unknown_Value_To_The_Next_Or_Last()
    {
        string[] choices = { "Auto", "Green" };
        Assert.Equal("Auto", FormScreen.Cycle(choices, "bogus", forward: true));
        Assert.Equal("Green", FormScreen.Cycle(choices, "bogus", forward: false));
    }

    // ----- FormScreen.Show -----

    [Fact]
    public void FormScreen_Escape_Cancels()
    {
        MutableField[] fields = { Field("Name", string.Empty) };

        IReadOnlyList<MutableField>? result = FormScreen.Show(
            Keys(Escape), Spool(out _), "Test", null, fields, "OK");

        Assert.Null(result);
    }

    [Fact]
    public void FormScreen_Typing_And_Backspace_Edit_The_Active_Field()
    {
        MutableField[] fields = { Field("Name", "bo") };

        // 'b' + Backspace leaves "bo"; Enter to confirm row, Enter to submit.
        IReadOnlyList<MutableField>? result = FormScreen.Show(
            Keys(Char('b'), Backspace, Enter, Enter), Spool(out _), "Test", null, fields, "OK");

        Assert.NotNull(result);
        Assert.Equal("bo", result[0].Value);
    }

    [Fact]
    public void FormScreen_Numeric_Fields_Ignore_Non_Digit_Keys()
    {
        MutableField[] fields = { Field("Port", "50", numeric: true) };

        // 'a' is ignored by the numeric filter; '0' is kept.
        IReadOnlyList<MutableField>? result = FormScreen.Show(
            Keys(Char('a'), Char('0'), Enter, Enter), Spool(out _), "Test", null, fields, "OK");

        Assert.NotNull(result);
        Assert.Equal("500", result[0].Value);
    }

    [Fact]
    public void FormScreen_Validation_Error_Blocks_Submit_Until_Fixed()
    {
        MutableField[] fields = { Field("Name", string.Empty) };
        static string? MustNotBeEmpty(IReadOnlyList<MutableField> f) =>
            f[0].Value.Length == 0 ? "Name cannot be empty." : null;

        IAnsiConsole console = Spool(out StringWriter writer);
        IReadOnlyList<MutableField>? result = FormScreen.Show(
            Keys(Enter, Enter, Up, Char('x'), Enter, Enter),
            console,
            "Test",
            null,
            fields,
            "OK",
            MustNotBeEmpty);

        Assert.NotNull(result);
        Assert.Equal("x", result[0].Value);
        Assert.Contains("Name cannot be empty.", writer.ToString());
    }

    [Fact]
    public void FormScreen_Number_Key_Picks_A_Choice_Then_Submits()
    {
        MutableField[] fields = { Field("Color", "Auto", choices: new[] { "Auto", "Green", "Cyan" }) };

        // '3' picks index 2 (Cyan); Enter advances, Enter submits.
        IReadOnlyList<MutableField>? result = FormScreen.Show(
            Keys(Char('3'), Enter, Enter), Spool(out _), "Test", null, fields, "OK");

        Assert.NotNull(result);
        Assert.Equal("Cyan", result[0].Value);
    }

    // ----- ConnectionMenu -----

    [Fact]
    public void ConnectionMenu_Escape_Returns_Null() =>
        Assert.Null(ConnectionMenu.Show(Keys(Escape), Spool(out _)));

    [Fact]
    public void ConnectionMenu_Typed_Player_Id_Yields_Expected_Settings()
    {
        ConnectionSettings? result = ConnectionMenu.Show(
            Keys(Down, Down, Char('a'), Char('l'), Char('i'), Char('c'), Char('e'), Enter, Enter, Enter),
            Spool(out _));

        Assert.NotNull(result);
        Assert.Equal("127.0.0.1", result.Value.Host);
        Assert.Equal(5000, result.Value.Port);
        Assert.Equal("alice", result.Value.PlayerId);
        Assert.Null(result.Value.PreferredColor);
        Assert.Equal(Args.DefaultConnectTimeoutSeconds, result.Value.TimeoutSeconds);
    }

    [Fact]
    public void ConnectionMenu_Number_Key_Selects_A_Chip_Color()
    {
        ConnectionSettings? result = ConnectionMenu.Show(
            Keys(Down, Down, Char('b'), Char('o'), Char('b'), Enter, Char('6'), Enter, Enter),
            Spool(out _));

        Assert.NotNull(result);
        Assert.Equal(PlayerColor.Cyan, result.Value.PreferredColor);
    }

    [Fact]
    public void ConnectionMenu_Validate_Rejects_Bad_Host_Port_And_Empty_Player_Id()
    {
        MutableField[] fields =
        {
            Field("Host", "two words"),
            Field("Port", "123", numeric: true),
            Field("Player id", string.Empty),
            Field("Chip color", "Auto"),
        };

        Assert.Contains("Invalid server host", ConnectionMenu.Validate(fields));

        fields[0].Value = "127.0.0.1";
        fields[1].Value = "99999";
        Assert.Contains("Invalid port", ConnectionMenu.Validate(fields));

        fields[1].Value = "5000";
        Assert.Equal("Player id cannot be empty.", ConnectionMenu.Validate(fields));

        fields[2].Value = "alice";
        Assert.Null(ConnectionMenu.Validate(fields));
    }

    // ----- HostForm -----

    [Fact]
    public void HostForm_Escape_Returns_Null() =>
        Assert.Null(HostForm.Show(Keys(Escape), Spool(out _)));

    [Fact]
    public void HostForm_Enter_Through_Uses_The_Default_Settings()
    {
        HostSettings? result = HostForm.Show(Keys(Enter, Enter, Enter, Enter, Enter), Spool(out _));

        Assert.NotNull(result);
        Assert.Equal(5000, result.Value.Port);
        Assert.Equal(IPAddress.Any, result.Value.Bind);
        Assert.Equal(GameStateStoreDefaultSave(), result.Value.SaveName);
    }

    [Fact]
    public void HostForm_Typed_Seed_And_Save_Overrides_The_Defaults()
    {
        HostSettings? result = HostForm.Show(
            Keys(Down, Down, Char('4'), Char('2'), Enter, Enter, Enter),
            Spool(out _));

        Assert.NotNull(result);
        Assert.Equal(42, result.Value.Seed);
        Assert.Equal(GameStateStoreDefaultSave(), result.Value.SaveName);
        Assert.Equal(IPAddress.Any, result.Value.Bind);
    }

    [Fact]
    public void HostForm_Validate_Rejects_Bad_Port_Bind_And_Seed()
    {
        MutableField[] fields =
        {
            Field("Port", "0", numeric: true),
            Field("Bind", "any"),
            Field("Seed", string.Empty),
            Field("Save", "game-001"),
        };

        Assert.Contains("Invalid port", HostForm.Validate(fields));

        fields[0].Value = "5000";
        fields[1].Value = "999.999.999.999";
        Assert.Contains("Invalid bind", HostForm.Validate(fields) ?? string.Empty);

        fields[1].Value = "loopback";
        fields[2].Value = "abc";
        Assert.Contains("Invalid seed", HostForm.Validate(fields));

        fields[2].Value = "42";
        Assert.Null(HostForm.Validate(fields));
    }

    private static string GameStateStoreDefaultSave() =>
        Sequence.Server.GameStateStore.DefaultSaveName;
}