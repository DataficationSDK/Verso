namespace Verso.Ado.Helpers;

/// <summary>
/// SQL dialect identifier used to drive dialect-conditional behavior such as
/// parameter scanning and local-name introduction rules.
/// </summary>
internal enum SqlDialect
{
    Unknown,
    SqlServer,
    Sqlite,
    Postgres,
    MySql,
    Oracle,
}
