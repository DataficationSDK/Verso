namespace Verso.Blazor.Services;

public sealed record ServerInputRequest(
    Guid CellId,
    string Prompt,
    bool IsPassword);
