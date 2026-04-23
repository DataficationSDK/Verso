using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.layout</c>. Prints or sets the default layout id used by .export.</summary>
public sealed class LayoutMeta : IMetaCommand
{
    public string Name => "layout";
    public string Summary => "Prints or sets the default export layout.";
    public string DetailedHelp =>
        ".layout [<id>|none]\n" +
        "  With no argument, prints the active layout id.\n" +
        "  With an id, sets the default ActiveLayoutId for subsequent .export calls.\n" +
        "  Pass 'none' to clear the layout.";

    public Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            var current = context.Session.ActiveLayoutId ?? "<none>";
            context.Console.MarkupLine($"Active layout: [bold]{Markup.Escape(current)}[/]");
            return Task.FromResult(true);
        }

        if (string.Equals(arg, "none", StringComparison.OrdinalIgnoreCase))
        {
            context.Session.ActiveLayoutId = null;
            context.Console.MarkupLine("[dim]Layout cleared.[/]");
            return Task.FromResult(true);
        }

        context.Session.ActiveLayoutId = arg;
        context.Console.MarkupLine($"Layout set to: [bold]{Markup.Escape(arg)}[/]");
        return Task.FromResult(true);
    }
}
