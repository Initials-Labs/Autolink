using Umbraco.Cms.Infrastructure.Scoping;

namespace Initials.AutoLink.Persistence;

/// <summary>
/// Tells a table that has not been created yet apart from a database that cannot be read.
/// </summary>
internal static class MissingTable
{
    /// <summary>
    /// True only when the table is confirmed absent. A check that itself fails reports false, so the caller's
    /// original error surfaces.
    /// </summary>
    public static bool Is(IScopeProvider scopeProvider, string tableName)
    {
        try
        {
            using IScope scope = scopeProvider.CreateScope(autoComplete: true);
            return !scope.SqlContext.SqlSyntax.DoesTableExist(scope.Database, tableName);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
