using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace XBullet.EasyTesting.EntityFrameworkCore;

internal static class DatabaseCleanup
{
    private const int MaximumAttempts = 4;

    internal static async Task EnsureDeletedAsync(
        DbContext database,
        CancellationToken cancellationToken)
    {
        if (!IsSqlite(database))
        {
            await database.Database.EnsureDeletedAsync(cancellationToken);
            return;
        }

        var dataSource = GetSqliteDataSource(database);
        await EnsureDeletedSqliteAsync(
            database.Database.ProviderName,
            dataSource,
            async () =>
            {
                ClearSqlitePools(database);
                await database.Database.EnsureDeletedAsync(cancellationToken);
            },
            cancellationToken);
    }

    internal static async Task EnsureDeletedSqliteAsync(
        string? providerName,
        string? dataSource,
        Func<Task> ensureDeleted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ensureDeleted);
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                await ensureDeleted();
                return;
            }
            catch (Exception exception) when (
                attempt < MaximumAttempts && IsTransientFileLock(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
            catch (Exception exception)
            {
                exception.Data[SqliteDatabaseCleanupDiagnostics.ExceptionDataKey] =
                    new SqliteDatabaseCleanupDiagnostics(
                        providerName,
                        dataSource,
                        attempt,
                        exception.GetType().FullName ?? exception.GetType().Name,
                        exception.Message);
                throw;
            }
        }
    }

    private static bool IsSqlite(DbContext database) =>
        database.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static string? GetSqliteDataSource(DbContext database)
    {
        try
        {
            var connection = database.Database.GetDbConnection();
            return connection.GetType().GetProperty("DataSource")?.GetValue(connection) as string;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void ClearSqlitePools(DbContext database)
    {
        try
        {
            var connectionType = database.Database.GetDbConnection().GetType();
            connectionType.GetMethod(
                    "ClearAllPools",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null)
                ?.Invoke(null, null);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or TargetInvocationException)
        {
            // A cleanup attempt still follows and will carry actionable failure diagnostics.
        }
    }

    private static bool IsTransientFileLock(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException or UnauthorizedAccessException)
            {
                return true;
            }

            if (string.Equals(
                    current.GetType().FullName,
                    "Microsoft.Data.Sqlite.SqliteException",
                    StringComparison.Ordinal))
            {
                var errorCode = current.GetType().GetProperty("SqliteErrorCode")?.GetValue(current);
                if (errorCode is 5 or 6)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
