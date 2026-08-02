using Spectre.Console;
using Verso.Cli.Resources;
using Verso.Cli.Utilities;

namespace Verso.Cli.Repl.Meta.Commands;

/// <summary>Implements <c>.kernel</c>. Prints the active kernel, or switches to another registered kernel.</summary>
public sealed class KernelMeta : IMetaCommand
{
    public string Name => "kernel";
    public string Summary => Strings.Meta_Kernel_Summary;

    // The first line is what the reader types, so it is written here rather than translated.
    public string DetailedHelp => ".kernel [<id>]\n" + Strings.Meta_Kernel_Details;

    public async Task<bool> ExecuteAsync(string argumentText, MetaContext context, CancellationToken ct)
    {
        var arg = argumentText.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            var current = context.Session.ActiveKernelId ?? Strings.Repl_MarkerNone;
            context.Console.MarkupLine(Messages.Typed(Strings.Meta_Kernel_Active, current));
            return true;
        }

        var kernels = context.Session.ExtensionHost.GetKernels();
        var match = kernels.FirstOrDefault(k => string.Equals(k.LanguageId, arg, StringComparison.OrdinalIgnoreCase))
                    ?? kernels.FirstOrDefault(k => string.Equals(k.ExtensionId, arg, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var known = string.Join(", ", kernels.Select(k => k.LanguageId));
            context.Console.MarkupLine(
                Messages.In("red", Messages.Say(Strings.Error_KernelNotRegistered, arg))
                + " " + Messages.Say(Strings.Error_AvailableKernels, known));
            return true;
        }

        context.Session.ActiveKernelId = match.LanguageId;
        context.Console.MarkupLine(Messages.Say(
            Strings.Meta_Kernel_Switched, match.LanguageId, match.DisplayName));

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
            context.Console.MarkupLine(Messages.In("yellow",
                Messages.Warning(Messages.Say(Strings.Meta_Kernel_WarmUpFailed, ex.Message))));
        }

        return true;
    }
}
