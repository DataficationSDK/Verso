using System.Management.Automation;
using System.Management.Automation.Host;

namespace Verso.PowerShell.Kernel.Host;

internal sealed class VersoPowerShellRawUserInterface : PSHostRawUserInterface
{
    private ConsoleColor _backgroundColor = ConsoleColor.Black;
    private Size _bufferSize = new(120, 3000);
    private Coordinates _cursorPosition;
    private int _cursorSize = 25;
    private ConsoleColor _foregroundColor = ConsoleColor.Gray;
    private Coordinates _windowPosition;
    private Size _windowSize = new(120, 40);
    private string _windowTitle = "Verso";

    public override ConsoleColor BackgroundColor
    {
        get => _backgroundColor;
        set => _backgroundColor = value;
    }

    public override Size BufferSize
    {
        get => _bufferSize;
        set => _bufferSize = value;
    }

    public override Coordinates CursorPosition
    {
        get => _cursorPosition;
        set => _cursorPosition = value;
    }

    public override int CursorSize
    {
        get => _cursorSize;
        set => _cursorSize = value;
    }

    public override ConsoleColor ForegroundColor
    {
        get => _foregroundColor;
        set => _foregroundColor = value;
    }

    public override bool KeyAvailable => false;

    public override Size MaxPhysicalWindowSize => new(240, 80);

    public override Size MaxWindowSize => new(240, 80);

    public override Coordinates WindowPosition
    {
        get => _windowPosition;
        set => _windowPosition = value;
    }

    public override Size WindowSize
    {
        get => _windowSize;
        set => _windowSize = value;
    }

    public override string WindowTitle
    {
        get => _windowTitle;
        set => _windowTitle = value;
    }

    public override void FlushInputBuffer()
    {
    }

    public override BufferCell[,] GetBufferContents(Rectangle rectangle)
    {
        var width = Math.Max(0, rectangle.Right - rectangle.Left + 1);
        var height = Math.Max(0, rectangle.Bottom - rectangle.Top + 1);
        return new BufferCell[height, width];
    }

    public override KeyInfo ReadKey(ReadKeyOptions options) =>
        throw new PSNotSupportedException("Reading raw keyboard input is not supported by Verso.");

    public override void ScrollBufferContents(
        Rectangle source,
        Coordinates destination,
        Rectangle clip,
        BufferCell fill)
    {
    }

    public override void SetBufferContents(Coordinates origin, BufferCell[,] contents)
    {
    }

    public override void SetBufferContents(Rectangle rectangle, BufferCell fill)
    {
    }
}
