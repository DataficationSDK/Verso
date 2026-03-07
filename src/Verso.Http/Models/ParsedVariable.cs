namespace Verso.Http.Models;

/// <summary>
/// An <c>@name = value</c> variable declaration from .http file syntax.
/// </summary>
internal sealed record ParsedVariable(string Name, string Value, int LineNumber);
