namespace Verso.Ado.Scaffold;

internal sealed record ScaffoldResult(
    string GeneratedCode,
    int EntityCount,
    int RelationshipCount,
    IReadOnlyList<string> EntityNames,
    string ContextClassName);
