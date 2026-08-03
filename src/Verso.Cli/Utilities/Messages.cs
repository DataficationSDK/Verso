using Spectre.Console;
using Verso.Cli.Resources;

namespace Verso.Cli.Utilities;

/// <summary>
/// Assembles the lines the CLI prints out of translated sentences.
/// </summary>
/// <remarks>
/// The words and the styling are kept apart on purpose. A translator is handed a sentence with
/// numbered placeholders in it and never sees a markup tag, so there is no tag to drop, mistype,
/// or leave unclosed, and a sentence whose emphasis falls on a different word in another language
/// still works because the placeholder travels with it.
/// <para>
/// Everything substituted in is escaped, because a file path or a kernel's error message can
/// contain a square bracket, which the terminal writer would otherwise read as a style.
/// </para>
/// </remarks>
internal static class Messages
{
    /// <summary>Prefixes a message with the word for something that could not be done.</summary>
    public static string Error(string message) => string.Format(Strings.Prefix_Error, message);

    /// <summary>Prefixes a message with the word for something worth knowing that stopped nothing.</summary>
    public static string Warning(string message) => string.Format(Strings.Prefix_Warning, message);

    /// <summary>Prefixes a message with the words for something that ended the session.</summary>
    public static string Fatal(string message) => string.Format(Strings.Prefix_Fatal, message);

    /// <summary>
    /// Fills a translated sentence in with values nobody chose the wording of: a file path, a
    /// count, a reason an exception gave. The result is safe to hand to a terminal writer.
    /// </summary>
    public static string Say(string sentence, params object?[] values)
        => string.Format(
            Markup.Escape(sentence),
            values.Select(v => (object)Markup.Escape(v?.ToString() ?? string.Empty)).ToArray());

    /// <summary>
    /// Fills a translated sentence in with things typed at a keyboard, drawing each in bold.
    /// </summary>
    /// <remarks>
    /// A command, an option, and a setting name are the same in every language, so they are the
    /// one part of a sentence that can be styled without the styling landing on the wrong word
    /// once the sentence around them has been rewritten.
    /// </remarks>
    public static string Typed(string sentence, params string[] typed)
        => string.Format(
            Markup.Escape(sentence),
            typed.Select(t => (object)$"[bold]{Markup.Escape(t)}[/]").ToArray());

    /// <summary>Draws a whole assembled line in one colour.</summary>
    /// <remarks>
    /// Colour goes on the line rather than on a word inside it. English puts the verb first, so
    /// <c>Saved</c> could be highlighted where it stood; a language that ends with its verb would
    /// leave the colour on whatever happened to be first instead.
    /// </remarks>
    public static string In(string style, string assembled) => $"[{style}]{assembled}[/]";
}
