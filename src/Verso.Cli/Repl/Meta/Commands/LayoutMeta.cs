using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.layout</c>. Prints or sets the default layout id used by .export.</summary>
public sealed class LayoutMeta : IMetaCommand
{
    public string Name => "layout";
    public string Summary => Strings.Meta_Layout_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".layout [<id>|none]\n" + Strings.Meta_Layout_Details;

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            var current = context.Session.ActiveLayoutId ?? Strings.Repl_MarkerNone;
            context.Console.MarkupLine(Messages.Typed(Strings.Meta_Layout_Active, current));
            return Task.FromResult(true);
        }

        if (string.Equals(arg, "none", StringComparison.OrdinalIgnoreCase))
        {
            context.Session.ActiveLayoutId = null;
            context.Console.MarkupLine(Messages.In("dim", Messages.Say(Strings.Meta_Layout_Cleared)));
            return Task.FromResult(true);
        }

        context.Session.ActiveLayoutId = arg;
        context.Console.MarkupLine(Messages.Typed(Strings.Meta_Layout_Set, arg));
        return Task.FromResult(true);
    }
}
