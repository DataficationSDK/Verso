using Verso.Abstractions;
using Verso.Python.Host;
using Verso.Python.Kernel;
using Verso.Python.Resources;

namespace Verso.Python.MagicCommands;

/// <summary>
/// <c>#!bind &lt;expression&gt;.&lt;trait&gt; [as &lt;name&gt;]</c> projects a widget's trait into the
/// notebook's shared variables, so a cell in any language reads it as an ordinary name and writing
/// it there moves the widget.
/// <list type="bullet">
/// <item><c>#!bind slider.value as threshold</c> shares the trait under a name of your choosing.</item>
/// <item><c>#!bind slider.value</c> shares it under the trait's own name.</item>
/// <item><c>#!bind --list</c> shows what is currently projected.</item>
/// <item><c>#!bind --remove threshold</c> stops projecting a name.</item>
/// </list>
/// The projection lasts as long as the interpreter holding the widget. A removed projection leaves
/// the shared variable holding the value it last had, so the cells reading it keep working.
/// </summary>
[VersoExtension]
public sealed class BindMagicCommand : IMagicCommand
{
    // --- IExtension (explicit for descriptive Name) ---

    public string ExtensionId => "verso.magic.bind";
    string IExtension.Name => Strings.Magic_Bind_Name;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";

    // --- IMagicCommand ---

    public string Name => "bind";
    public string Description => Strings.Magic_Bind_Description;

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new[]
    {
        new ParameterDefinition("target", Strings.Magic_Bind_Param_Target, typeof(string)),
        new ParameterDefinition("name", Strings.Magic_Bind_Param_Name, typeof(string)),
    };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task ExecuteAsync(string arguments, IMagicCommandContext context)
    {
        // The cell's remaining code still runs: a bind belongs beside the lines that use it.
        context.SuppressExecution = false;

        var argument = (arguments ?? string.Empty).Trim();
        if (argument.Length == 0)
        {
            await context.WriteOutputAsync(CellOutput.Error(Strings.Magic_Bind_Usage)).ConfigureAwait(false);
            return;
        }

        if (argument is "--list" or "-l")
        {
            await ListAsync(context).ConfigureAwait(false);
            return;
        }

        if (argument.StartsWith("--remove", StringComparison.Ordinal))
        {
            await RemoveAsync(argument["--remove".Length..].Trim(), context).ConfigureAwait(false);
            return;
        }

        await BindAsync(argument, context).ConfigureAwait(false);
    }

    // --- binding ---

    private static async Task BindAsync(string argument, IMagicCommandContext context)
    {
        if (!TryParse(argument, out var expression, out var trait, out var name, out var problem))
        {
            await context.WriteOutputAsync(CellOutput.Error(problem!)).ConfigureAwait(false);
            return;
        }

        var session = await FindSessionAsync(context).ConfigureAwait(false);
        if (session is null)
        {
            await context.WriteOutputAsync(CellOutput.Error(Strings.Magic_Bind_NoSession))
                .ConfigureAwait(false);
            return;
        }

        var outcome = await session
            .BindAsync(context, expression!, trait!, name, context.CancellationToken)
            .ConfigureAwait(false);

        if (!outcome.Succeeded || outcome.Projection is null)
        {
            await context.WriteOutputAsync(CellOutput.Error(
                    string.Format(Strings.Magic_Bind_Failed, outcome.Reason ?? "")))
                .ConfigureAwait(false);
            return;
        }

        var message = string.Format(
            Strings.Magic_Bind_Bound,
            outcome.Projection.Name, outcome.Projection.Expression, outcome.Projection.Trait);

        if (!string.IsNullOrEmpty(outcome.Replaced)
            && !string.Equals(outcome.Replaced, outcome.Projection.Name, StringComparison.OrdinalIgnoreCase))
        {
            message += " " + string.Format(Strings.Magic_Bind_Replaced, outcome.Replaced);
        }

        await context.WriteOutputAsync(CellOutput.Plain(message)).ConfigureAwait(false);
    }

    private static async Task RemoveAsync(string name, IMagicCommandContext context)
    {
        if (name.Length == 0)
        {
            await context.WriteOutputAsync(CellOutput.Error(Strings.Magic_Bind_RemoveUsage))
                .ConfigureAwait(false);
            return;
        }

        var session = await FindSessionAsync(context).ConfigureAwait(false);
        if (session is null)
        {
            await context.WriteOutputAsync(CellOutput.Error(Strings.Magic_Bind_NoSession))
                .ConfigureAwait(false);
            return;
        }

        var outcome = await session.UnbindAsync(name, context.CancellationToken).ConfigureAwait(false);
        await context.WriteOutputAsync(outcome.Succeeded
                ? CellOutput.Plain(string.Format(Strings.Magic_Bind_Removed, name))
                : CellOutput.Error(outcome.Reason ?? string.Format(Strings.Magic_Bind_NotFound, name)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// List what is projected. Answered from what the managing side holds rather than by asking
    /// the interpreter, so it reads the same during a long cell as it does between two.
    /// </summary>
    private static async Task ListAsync(IMagicCommandContext context)
    {
        var session = await FindSessionAsync(context).ConfigureAwait(false);
        var projections = session?.Projections ?? Array.Empty<VariableProjection>();

        if (projections.Count == 0)
        {
            await context.WriteOutputAsync(CellOutput.Plain(Strings.Magic_Bind_None)).ConfigureAwait(false);
            return;
        }

        var lines = new List<string> { Strings.Magic_Bind_Heading };
        foreach (var projection in projections)
            lines.Add($"  {projection.Name}  <-  {projection.Expression}.{projection.Trait}");

        await context.WriteOutputAsync(CellOutput.Plain(string.Join("\n", lines))).ConfigureAwait(false);
    }

    // --- parsing ---

    /// <summary>
    /// Read <c>&lt;expression&gt;.&lt;trait&gt; [as &lt;name&gt;]</c>.
    /// </summary>
    /// <remarks>
    /// The object is split from the trait at the last dot rather than the first, so
    /// <c>panel.layout.width</c> binds the width of the layout rather than looking for a trait
    /// called <c>layout.width</c>. Everything to the left is handed to the interpreter as it was
    /// written and resolved there, which is what lets an index or a call name the widget.
    /// </remarks>
    internal static bool TryParse(
        string argument, out string? expression, out string? trait, out string? name, out string? problem)
    {
        expression = null;
        trait = null;
        name = null;
        problem = null;

        var target = argument;
        var separator = FindAsSeparator(argument);
        if (separator >= 0)
        {
            target = argument[..separator].TrimEnd();
            name = argument[(separator + 4)..].Trim();

            if (name.Length == 0)
            {
                problem = Strings.Magic_Bind_Usage;
                return false;
            }

            if (!IsUsableName(name))
            {
                problem = string.Format(Strings.Magic_Bind_BadName, name);
                return false;
            }
        }

        var dot = target.LastIndexOf('.');
        if (dot <= 0 || dot == target.Length - 1)
        {
            problem = string.Format(Strings.Magic_Bind_NeedsTrait, target);
            return false;
        }

        expression = target[..dot].Trim();
        trait = target[(dot + 1)..].Trim();

        if (expression.Length == 0 || trait.Length == 0)
        {
            problem = string.Format(Strings.Magic_Bind_NeedsTrait, target);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Where the <c>as</c> keyword sits, or -1 when there is none. Matched as a whole word so an
    /// expression that merely contains the letters, such as <c>things["gas"].value</c>, is left
    /// alone, and taken from the right so the last one wins.
    /// </summary>
    private static int FindAsSeparator(string argument)
    {
        for (var index = argument.Length - 4; index >= 1; index--)
        {
            if (argument[index] is not ' ' and not '\t')
                continue;

            if (argument[index + 1] is not 'a' || argument[index + 2] is not 's')
                continue;

            if (argument[index + 3] is not ' ' and not '\t')
                continue;

            return index;
        }

        return -1;
    }

    /// <summary>
    /// Whether a name is one every kernel can reach. A shared variable is read as an identifier in
    /// languages that have no way to spell anything else, so a name with a space or a dot in it
    /// would be written and never readable.
    /// </summary>
    private static bool IsUsableName(string name)
    {
        if (name.Length == 0 || (!char.IsLetter(name[0]) && name[0] != '_'))
            return false;

        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
                return false;
        }

        return true;
    }

    private static async Task<PythonHostSession?> FindSessionAsync(IMagicCommandContext context)
    {
        PythonKernel? kernel;
        try { kernel = context.ExtensionHost?.GetKernels().OfType<PythonKernel>().FirstOrDefault(); }
        catch { return null; }

        if (kernel is null)
            return null;

        return await kernel.GetBoundSessionAsync(context, context.CancellationToken).ConfigureAwait(false);
    }
}
