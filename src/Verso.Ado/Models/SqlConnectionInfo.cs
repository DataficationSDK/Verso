using System.Data.Common;

namespace Verso.Ado.Models;

internal sealed record SqlConnectionInfo(
    string Name,
    string ConnectionString,
    string? ProviderName,
    DbConnection? Connection);
