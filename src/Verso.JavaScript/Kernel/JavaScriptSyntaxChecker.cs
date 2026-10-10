using Acornima;
using Verso.Abstractions;

namespace Verso.JavaScript.Kernel;

/// <summary>
/// Reports JavaScript syntax errors for the editor using Acornima, the parser Jint is built on.
/// Runs in-process, so it needs neither Node.js nor an initialized kernel.
/// </summary>
/// <remarks>
/// A cell runs inside a function body, so top-level <c>return</c> and <c>await</c> are accepted.
/// It is parsed as a script rather than a module because that wrapping is also why a static
/// <c>import</c> fails when the cell runs; flagging it here says so before the run does.
/// </remarks>
internal static class JavaScriptSyntaxChecker
{
    // The default language version already covers using declarations, import attributes and
    // the newer regular expression syntax, so no experimental features are switched on.
    private static readonly ParserOptions Options = new()
    {
        AllowReturnOutsideFunction = true,
        AllowAwaitOutsideFunction = true,
        AllowTopLevelUsing = true,
    };

    public static IReadOnlyList<Diagnostic> Check(string code)
    {
        try
        {
            // A parser holds state for the parse in progress, so each call gets its own.
            new Parser(Options).ParseScript(code);
            return Array.Empty<Diagnostic>();
        }
        catch (ParseErrorException ex)
        {
            return [ToDiagnostic(ex)];
        }
        catch
        {
            // Anything else is a fault in the parser rather than in the cell, and the editor
            // is better off showing nothing than a marker the code did not earn.
            return Array.Empty<Diagnostic>();
        }
    }

    private static Diagnostic ToDiagnostic(ParseErrorException ex)
    {
        var message = string.IsNullOrEmpty(ex.Description) ? ex.Message : ex.Description;
        var code = string.IsNullOrEmpty(ex.Error?.Code) ? null : ex.Error!.Code;

        // Acornima's line numbers are one-based and its columns zero-based. A position it could
        // not work out is reported as line zero, which pins the marker to the start of the cell.
        if (ex.LineNumber <= 0)
            return new Diagnostic(DiagnosticSeverity.Error, message, 0, 0, 0, 0, code);

        var line = Math.Max(0, ex.LineNumber - 1);
        var column = Math.Max(0, ex.Column);
        return new Diagnostic(DiagnosticSeverity.Error, message, line, column, line, column + 1, code);
    }
}
