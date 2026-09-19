namespace XBullet.EasyTesting.EntityFrameworkCore;

/// <summary>Describes a terminal failure while deleting a SQLite scenario database.</summary>
public sealed class SqliteDatabaseCleanupDiagnostics
{
    /// <summary>Creates cleanup diagnostics.</summary>
    public SqliteDatabaseCleanupDiagnostics(
        string? providerName,
        string? dataSource,
        int attempts,
        string exceptionType,
        string message)
    {
        ProviderName = providerName;
        DataSource = dataSource;
        Attempts = attempts;
        ExceptionType = exceptionType;
        Message = message;
    }

    /// <summary>The exception-data key containing cleanup diagnostics.</summary>
    public const string ExceptionDataKey =
        "XBullet.EasyTesting.EntityFrameworkCore.SqliteDatabaseCleanupDiagnostics";

    /// <summary>Gets the EF Core database provider.</summary>
    public string? ProviderName { get; }

    /// <summary>Gets the SQLite data source when it can be determined safely.</summary>
    public string? DataSource { get; }

    /// <summary>Gets the number of deletion attempts made.</summary>
    public int Attempts { get; }

    /// <summary>Gets the terminal exception type.</summary>
    public string ExceptionType { get; }

    /// <summary>Gets the terminal exception message.</summary>
    public string Message { get; }
}
