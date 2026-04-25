using Spectre.Console;
using Spectre.Console.Json;
using Spectre.Console.Rendering;
using Verso.Abstractions;

namespace Verso.Cli.Repl.Rendering.Renderers;

internal static class JsonRenderer
{
    public static IRenderable AsRenderable(CellOutput output, TruncationPolicy policy, bool useColor)
    {
        var content = output.Content ?? string.Empty;
        if (!useColor)
            return new Text(policy.ClipLines(content));

        try
        {
            return new JsonText(content);
        }
        catch
        {
            // JsonText throws on malformed JSON — fall back to plain so we don't lose the output.
            return new Text(policy.ClipLines(content));
        }
    }
}
