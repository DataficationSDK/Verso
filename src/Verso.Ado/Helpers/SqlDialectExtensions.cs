namespace Verso.Ado.Helpers;

/// <summary>
/// Dialect-dependent conventions shared by the parameter scanner, the kernel's
/// binding path, and diagnostics.
/// </summary>
internal static class SqlDialectExtensions
{
    /// <summary>
    /// The character that introduces a named bind parameter in the given dialect.
    /// Oracle uses a leading colon (<c>:name</c>); every other supported dialect
    /// uses an at-sign (<c>@name</c>). This is the single source of truth so the
    /// scanner, binder, and diagnostic messages stay in agreement.
    /// </summary>
    internal static char ParameterPrefix(this SqlDialect dialect) =>
        dialect == SqlDialect.Oracle ? ':' : '@';
}
