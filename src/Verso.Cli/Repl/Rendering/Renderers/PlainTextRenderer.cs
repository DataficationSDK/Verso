using Spectre.Console;
using Spectre.Console.Rendering;
using Verso.Abstractions;

namespace Verso.Cli.Repl.Rendering.Renderers;

internal static class PlainTextRenderer
{
    public static IRenderable AsRenderable(CellOutput output, TruncationPolicy policy)
    {
        return new Text(policy.ClipLines(output.Content ?? string.Empty));
    }
}
