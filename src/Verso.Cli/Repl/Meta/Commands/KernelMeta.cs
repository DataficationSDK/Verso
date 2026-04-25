using Spectre.Console;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.kernel</c>. Prints the active kernel, or switches to another registered kernel.</summary>
public sealed class KernelMeta : IMetaCommand
{
    public string Name => "kernel";
    public string Summary => "Prints or switches the active kernel.";
    public string DetailedHelp =>
        ".kernel [<id>]\n" +
        "  With no argument, prints the active kernel.\n" +
        "  With an id (LanguageId, matched case-insensitively), switches the active kernel\n" +
        "  for subsequent cells. Variables already declared in the prior kernel remain in\n" +
        "  that kernel's scope.";

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            var current = context.Session.ActiveKernelId ?? "<none>";
            context.Console.MarkupLine($"Active kernel: [bold]{Markup.Escape(current)}[/]");
            return true;
        }

        var kernels = context.Session.ExtensionHost.GetKernels();
        var match = kernels.FirstOrDefault(k => string.Equals(k.LanguageId, arg, StringComparison.OrdinalIgnoreCase))
                    ?? kernels.FirstOrDefault(k => string.Equals(k.ExtensionId, arg, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var known = string.Join(", ", kernels.Select(k => k.LanguageId));
            context.Console.MarkupLine($"[red]Kernel '{Markup.Escape(arg)}' is not registered.[/] Available: {Markup.Escape(known)}");
            return true;
        }

        context.Session.ActiveKernelId = match.LanguageId;
        context.Console.MarkupLine($"Switched to kernel: [bold]{Markup.Escape(match.LanguageId)}[/] ({Markup.Escape(match.DisplayName)})");

        // Eagerly initialize the kernel so the first keystroke doesn't hit a
        // cold-start on the completion path. Without this, completion for CSharp
        // in particular blocks on MEF/Workspace construction during the first
        // GetCompletionsAsync call and the popup appears empty.
        try
        {
            await context.Session.Scaffold.WarmUpKernelAsync(match.LanguageId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Console.MarkupLine($"[yellow]Warning: kernel warm-up failed: {Markup.Escape(ex.Message)}[/]");
        }

        return true;
    }
}
