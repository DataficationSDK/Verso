using System.CommandLine;
using Verso.Localization;

namespace Verso.Cli.Utilities;

/// <summary>
/// The shared <c>--language</c> option. Without it the language comes from
/// <c>VERSO_LANGUAGE</c>, then from the operating system, then English.
/// </summary>
public static class LanguageOption
{
    /// <summary>
    /// The one option instance, registered globally on the root command.
    /// </summary>
    /// <remarks>
    /// Unlike the other shared options, which each command creates its own copy of, this one is
    /// global. It has to be accepted before a subcommand name so that <c>verso --language de
    /// --help</c> works, and a global option is also the only way for every subcommand to read
    /// the same instance out of a parse result.
    /// </remarks>
    public static Option<string?> Instance { get; } = new(
        VersoCultures.Option,
        $"Language for messages and help, one of: {string.Join(", ", VersoCultures.Supported)}. " +
        "Defaults to the system language, falling back to English.");

    /// <summary>
    /// Applies the language named in the raw arguments, before anything is parsed.
    /// </summary>
    /// <remarks>
    /// Command and option descriptions are read while the command tree is built, which happens
    /// before the parser has looked at anything, so <c>verso --language de --help</c> only works
    /// if the language is settled first. That rules out reading it from a parse result, hence the
    /// scan.
    /// <para>
    /// The interface language moves and the formatting culture does not. A notebook run here
    /// writes its results to the console and back into the file, so letting a language option
    /// change the decimal separator would change the output rather than translate it.
    /// </para>
    /// </remarks>
    /// <param name="args">The process arguments.</param>
    public static void ApplyFromArguments(string[] args)
        => VersoCultures.ApplyUiCulture(VersoCultures.Resolve(VersoCultures.FromArguments(args)));
}
