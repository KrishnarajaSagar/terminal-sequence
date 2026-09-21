using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Sequence.Domain;
using Spectre.Console;

namespace Sequence.Terminal;

/// <summary>Kinds of input the pump can deliver. Mouse moves never surface; they only move the highlight.</summary>
public enum InputEventKind
{
    /// <summary>A completed line of text (Enter pressed, or a line came from a pipe/redirect).</summary>
    Line,

    /// <summary>A left click on one of the hand's tiles/cards (<see cref="InputEvent.HandIndex"/>, 1-based).</summary>
    HandClick,

    /// <summary>A left click on a board cell (<see cref="InputEvent.Board"/>).</summary>
    BoardClick,
}

/// <summary>One unit of input produced by an <see cref="ITurnPump"/>.</summary>
public sealed record InputEvent(InputEventKind Kind, string? Line, int? HandIndex, BoardPosition? Board)
{
    public static InputEvent Completed(string? text) => new(InputEventKind.Line, text, null, null);

    public static InputEvent HandClick(int index) => new(InputEventKind.HandClick, null, index, null);

    public static InputEvent BoardClick(BoardPosition position) => new(InputEventKind.BoardClick, null, null, position);
}

/// <summary>
/// Source of the per-keystroke input the prompting flow consumes. The redirected
/// implementation reads whole lines like the original client; the interactive one reads
/// raw console input so it can watch for mouse events while the player types.
/// </summary>
public interface ITurnPump : IDisposable
{
    /// <summary>
    /// Blocks for the next consumable event. A non-null <paramref name="prompt"/> is
    /// printed (once, each call) before waiting; pass null for the phases that reuse the
    /// prompt already drawn by the renderer.
    /// </summary>
    InputEvent Next(string? prompt);

    /// <summary>The render context changes (new view, footer...). Hover survives.</summary>
    void BeginTurn(PlayerView view, int sequenceTarget, string? footer, PlayerColor hoverColor);

    /// <summary>The board cell currently under the pointer, if any.</summary>
    BoardPosition? Hover { get; }

    /// <summary>The hand card currently under the pointer, if any.</summary>
    int? HoveredHand { get; }

    /// <summary>The viewer colour used to tint hover highlights.</summary>
    PlayerColor HoverColor { get; }
}

/// <summary>Creates the right pump for the console the process is attached to.</summary>
public static class TurnPump
{
    public static ITurnPump Create(PlayerView view, int sequenceTarget, string? footer)
    {
        if (!MouseSupport.IsAvailable)
        {
            return RedirectedTurnPump.Instance;
        }

        try
        {
            return new InteractiveTurnPump(view, sequenceTarget, footer);
        }
        catch (IOException)
        {
            // The console could not be opened for raw input; fall back to line reads.
            return RedirectedTurnPump.Instance;
        }
    }
}

/// <summary>
/// The classic keyboard path: blocks on <c>Console.ReadLine</c>. Used whenever the
/// process is attached to a pipe (tests, smokes, CI) or mouse input is disabled.
/// </summary>
public sealed class RedirectedTurnPump : ITurnPump
{
    public static readonly RedirectedTurnPump Instance = new();

    private RedirectedTurnPump()
    {
    }

    public bool MouseEnabled => false;

    public BoardPosition? Hover => null;

    public int? HoveredHand => null;

    public PlayerColor HoverColor => PlayerColor.Red;

    public void BeginTurn(PlayerView view, int sequenceTarget, string? footer, PlayerColor hoverColor)
    {
    }

    public InputEvent Next(string? prompt)
    {
        if (prompt is not null)
        {
            Console.Write(prompt);
        }

        return InputEvent.Completed(Console.ReadLine());
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Reads raw console input (no echo, no line buffering) so keystrokes arrive one at a
/// time and xterm mouse sequences can be decoded. Printable keys drive a small line
/// editor that echoes back what is typed; mouse moves repaint the hovered board cell.
/// </summary>
public sealed class InteractiveTurnPump : ITurnPump
{
    private readonly Stream _input;
    private readonly StringBuilder _line = new();
    private readonly bool _restoreConsoleMode;
    private BoardGeometry _lastGeometry;

    private PlayerView _view;
    private int _sequenceTarget;
    private string? _footer;
    private PlayerColor _hoverColor;
    private string? _currentPrompt;
    private BoardPosition? _hover;
    private int? _hoverHand;

    internal InteractiveTurnPump(PlayerView view, int sequenceTarget, string? footer)
    {
        _view = view;
        _sequenceTarget = sequenceTarget;
        _footer = footer;
        _hoverColor = view.Viewer.Color;
        _lastGeometry = BoardGeometry.ComputeCurrent();

        if (OperatingSystem.IsWindows())
        {
            _restoreConsoleMode = RawConsoleInput.TryEnter(out _);
        }

        _input = Console.OpenStandardInput();
        MouseSupport.Enable();
    }

    public BoardPosition? Hover => _hover;

    public int? HoveredHand => _hoverHand;

    public PlayerColor HoverColor => _hoverColor;

    public void BeginTurn(PlayerView view, int sequenceTarget, string? footer, PlayerColor hoverColor)
    {
        _view = view;
        _sequenceTarget = sequenceTarget;
        _footer = footer;
        _hoverColor = hoverColor;
        if (_hoverHand is int hand && hand > view.Hand.Count)
        {
            _hoverHand = null;
        }
    }

    public InputEvent Next(string? prompt)
    {
        if (prompt is not null)
        {
            _currentPrompt = prompt;
            Console.Write(prompt);
        }

        while (true)
        {
            int first = ReadByte();
            if (first < 0)
            {
                return InputEvent.Completed(null); // EOF from the pipe/console
            }

            if (first == 0x1b)
            {
                if (TryReadEscapeSequence(out InputEvent? click))
                {
                    return click!;
                }

                continue;
            }

            if (first == 3) // Ctrl+C
            {
                return InputEvent.Completed("quit");
            }

            if (first is 8 or 127) // Backspace / DEL
            {
                Backspace();
                continue;
            }

            if (first == 13) // Enter: the line is complete.
            {
                string line = _line.ToString();
                _line.Clear();
                return InputEvent.Completed(line);
            }

            if (first == 10) // LF half of a CRLF pair
            {
                continue;
            }

            AppendCharacter(first);
        }
    }

    public void Dispose()
    {
        MouseSupport.Disable();
        if (_restoreConsoleMode)
        {
            RawConsoleInput.Restore();
        }

        try
        {
            _input.Dispose();
        }
        catch (IOException)
        {
            // already closed
        }
    }

    // ------------------------------------------------------------- character entry

    private int ReadByte()
    {
        try
        {
            return _input.ReadByte();
        }
        catch (IOException)
        {
            return -1;
        }
    }

    private void AppendCharacter(int first)
    {
        if (first < 128)
        {
            char c = (char)first;
            _line.Append(c);
            Console.Write(c);
            return;
        }

        int count = GetUtf8SequenceLength(first);
        var bytes = new List<byte> { (byte)first };
        for (int i = 1; i < count; i++)
        {
            int next = ReadByte();
            if (next < 0)
            {
                break;
            }

            bytes.Add((byte)next);
        }

        string text = Encoding.UTF8.GetString(bytes.ToArray());
        if (text.Length == 0)
        {
            return;
        }

        _line.Append(text);
        Console.Write(text);
    }

    private static int GetUtf8SequenceLength(int first)
    {
        if ((first & 0b1110_0000) == 0b1100_0000)
        {
            return 2;
        }

        if ((first & 0b1111_0000) == 0b1110_0000)
        {
            return 3;
        }

        if ((first & 0b1111_1000) == 0b1111_0000)
        {
            return 4;
        }

        return 1; // not a valid UTF-8 lead byte; keep it as an 8-bit character
    }

    private void Backspace()
    {
        if (_line.Length == 0)
        {
            return;
        }

        int last = _line.Length - 1;
        while (last > 0 && char.IsLowSurrogate(_line[last]))
        {
            last--;
        }

        int removed = _line.Length - last;
        _line.Remove(last, removed);
        Console.Write("\b \b");
        if (removed == 2) // an astral character occupies two visual cells on Windows
        {
            Console.Write("\b \b");
        }
    }

    // ------------------------------------------------------------- escape sequences

    /// <summary>
    /// Consumes the bytes that follow ESC. Returns true when the sequence was a usable
    /// left-click; <paramref name="click"/> then carries the resulting event.
    /// </summary>
    private bool TryReadEscapeSequence(out InputEvent? click)
    {
        click = null;
        int header = ReadByte();
        if (header < 0)
        {
            return false;
        }

        switch (header)
        {
            case (int)'[':
                return ReadCsi(out click);

            case (int)'O':
                _ = ReadByte(); // SS3 (function keys)
                return false;

            case (int)']':
                SwallowOsc();
                return false;

            default:
                // A bare/malformed ESC: treat it as an ignored key press.
                return false;
        }
    }

    /// <summary>XTerm (SGR) mouse: <c>ESC[&lt;b;cx;cyM</c> (press) or <c>...m</c> (release).</summary>
    private bool ReadCsi(out InputEvent? click)
    {
        click = null;
        int intro = ReadByte();
        if (intro < 0)
        {
            return false;
        }

        if (intro == '<')
        {
            return ReadSgrMouse(out click);
        }

        // Any other CSI sequence (arrow keys, Home...): swallow up to the final byte.
        int b = intro;
        while (b >= 0 && !(b is >= 0x40 and <= 0x7e))
        {
            b = ReadByte();
        }

        return false;
    }

    private bool ReadSgrMouse(out InputEvent? click)
    {
        click = null;
        var parameters = new StringBuilder();
        int final = 0;
        while (final == 0)
        {
            int b = ReadByte();
            if (b < 0)
            {
                return false;
            }

            if (b is >= 0x40 and <= 0x7e)
            {
                final = b;
                break;
            }

            parameters.Append((char)b);
        }

        if (!TryDecodeSgrMouse(parameters.ToString(), (char)final, out int button, out int col, out int row))
        {
            return false;
        }

        HandleMouseEvent(button, col - 1, row - 1, final == 'M');
        if (IsLeftClickPress(button))
        {
            click = ResolveClick(col - 1, row - 1);
        }

        return click is not null;
    }

    /// <summary>
    /// Decodes the SGR (xterm 1006) mouse payload that follows <c>ESC[&lt;</c>: three
    /// <c>;</c>-separated parameters, button code (bit 5 = motion, bits 0-1 = button),
    /// column and row, both 1-based. Terminated by <c>M</c> (press) or <c>m</c> (release).
    /// </summary>
    public static bool TryDecodeSgrMouse(string parameters, char terminator, out int button, out int col, out int row)
    {
        button = 0;
        col = 0;
        row = 0;

        string[] parts = parameters.Split(';');
        if (parts.Length < 3 ||
            (terminator is not (char)'M' and not (char)'m') ||
            !int.TryParse(parts[0], out button) ||
            !int.TryParse(parts[1], out col) ||
            !int.TryParse(parts[2], out row))
        {
            return false;
        }

        return true;
    }

    private static bool IsLeftClickPress(int button) =>
        button is >= 0 and < 32 && (button & 3) == 0; // <32 = a press (not motion), bits 0-1 = 0 = left

    private void SwallowOsc()
    {
        // OSC runs until BEL or ESC \.
        int previous = 0;
        while (true)
        {
            int b = ReadByte();
            if (b < 0)
            {
                return;
            }

            if (b == 7 || (previous == 0x1b && b == '\\'))
            {
                return;
            }

            previous = b;
        }
    }

    // ------------------------------------------------------------- hover + clicks

    /// <summary>Points the highlight at (row, col), repainting any cell that changed.</summary>
    private void HandleMouseEvent(int button, int row, int col, bool isPress)
    {
        _ = button;
        _ = isPress;
        UpdateHover(row, col);
    }

    private void UpdateHover(int row, int col)
    {
        BoardGeometry geometry = BoardGeometry.ComputeCurrent();
        if (geometry.Width != _lastGeometry.Width || geometry.Height != _lastGeometry.Height)
        {
            _lastGeometry = geometry;
            _hover = null;
            _hoverHand = null;
            RepaintWithPrompt();
        }

        BoardPosition? nextBoard = geometry.TryHitTestBoard(row, col, out BoardPosition position) ? position : null;
        int? nextHand = geometry.TryHitTestHand(row, col, _view.Hand.Count, _view.HasExchangedDeadCardThisTurn, out int hand) ? hand : null;

        if (nextBoard == _hover && nextHand == _hoverHand)
        {
            return;
        }

        if (_hover is BoardPosition oldPosition)
        {
            TerminalRenderer.PatchCell(_view, oldPosition, false, _hoverColor, geometry);
        }

        if (_hoverHand is int oldHand)
        {
            TerminalRenderer.PatchHandCard(_view, oldHand, false, _hoverColor, geometry);
        }

        if (nextBoard is BoardPosition newPosition)
        {
            TerminalRenderer.PatchCell(_view, newPosition, true, _hoverColor, geometry);
        }

        if (nextHand is int newHand)
        {
            TerminalRenderer.PatchHandCard(_view, newHand, true, _hoverColor, geometry);
        }

        _hover = nextBoard;
        _hoverHand = nextHand;
    }

    private InputEvent? ResolveClick(int row, int col)
    {
        BoardGeometry geometry = BoardGeometry.ComputeCurrent();
        if (geometry.TryHitTestBoard(row, col, out BoardPosition position))
        {
            return InputEvent.BoardClick(position);
        }

        if (geometry.TryHitTestHand(row, col, _view.Hand.Count, _view.HasExchangedDeadCardThisTurn, out int hand))
        {
            return InputEvent.HandClick(hand);
        }

        return null;
    }

    private void RepaintWithPrompt()
    {
        try
        {
            TerminalRenderer.RenderFullScreen(_view, _sequenceTarget, _footer, _hover, _hoverColor, _hoverHand);
        }
        catch (IOException)
        {
            return;
        }

        if (_currentPrompt is not null)
        {
            Console.Write("\r\n" + _currentPrompt);
        }

        Console.Write(_line.ToString());
    }

    /// <summary>
    /// Puts the Windows console into raw-ish input mode (no echo, no line buffering,
    /// VT input enabled so SGR mouse events arrive as bytes) and restores it on exit.
    /// </summary>
    private static class RawConsoleInput
    {
        private const uint StdInputHandle = unchecked((uint)-10);
        private const uint EnableLineInput = 0x0002;
        private const uint EnableEchoInput = 0x0004;
        private const uint EnableProcessedInput = 0x0001;
        private const uint EnableMouseInput = 0x0010;
        private const uint EnableExtendedFlags = 0x0080;
        private const uint EnableQuickEditMode = 0x0040;
        private const uint EnableVirtualTerminalInput = 0x0200;

        private static IntPtr _inputHandle;
        private static uint _originalMode;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(uint nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        /// <summary>Switches the input console to raw mode. False when there is no console.</summary>
        public static bool TryEnter(out uint originalMode)
        {
            originalMode = 0;
            IntPtr handle = GetStdHandle(StdInputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1) || !GetConsoleMode(handle, out uint mode))
            {
                return false;
            }

            uint desired = (mode | EnableMouseInput | EnableVirtualTerminalInput | EnableExtendedFlags) &
                           ~(EnableLineInput | EnableEchoInput | EnableProcessedInput | EnableQuickEditMode);
            if (desired == mode)
            {
                _inputHandle = handle;
                _originalMode = mode;
                return true;
            }

            if (!SetConsoleMode(handle, desired))
            {
                return false;
            }

            _inputHandle = handle;
            _originalMode = mode;
            return true;
        }

        public static void Restore()
        {
            if (_inputHandle == IntPtr.Zero)
            {
                return;
            }

            _ = SetConsoleMode(_inputHandle, _originalMode);
            _inputHandle = IntPtr.Zero;
        }
    }
}