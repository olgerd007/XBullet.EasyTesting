namespace XBullet.EasyTesting.EntityFrameworkCore;

/// <summary>Describes a terminal failure while deleting a SQLite scenario database.</summary>
public sealed class SqliteDatabaseCleanupDiagnostics
{
    /// <summary>Creates cleanup diagnostics.</summary>
    /// <param name="providerName">The EF Core provider name, or <see langword="null"/> when unavailable.</param>
    /// <param name="dataSource">
    /// The SQLite data-source name or path, or <see langword="null"/> when it cannot be determined safely.
    /// </param>
    /// <param name="attempts">The number of deletion attempts made before the terminal failure.</param>
    /// <param name="exceptionType">The full terminal exception type name when available, otherwise its short name.</param>
    /// <param name="message">The terminal exception message, which can be empty.</param>
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
    /// <value>The provider name, or <see langword="null"/> when it was unavailable.</value>
    public string? ProviderName { get; }

    /// <summary>Gets the SQLite data source when it can be determined safely.</summary>
    /// <value>
    /// The SQLite data-source name or path, or <see langword="null"/> when unavailable. This value
    /// can expose filesystem or connection details and should be handled as diagnostic data.
    /// </value>
    public string? DataSource { get; }

    /// <summary>Gets the number of deletion attempts made.</summary>
    /// <value>The number of attempts completed before the terminal failure.</value>
    public int Attempts { get; }

    /// <summary>Gets the terminal exception type.</summary>
    /// <value>The full exception type name when available, otherwise its short name.</value>
    public string ExceptionType { get; }

    /// <summary>Gets the terminal exception message.</summary>
    /// <value>The exception message, which can be empty and may contain sensitive diagnostic data.</value>
    public string Message { get; }
}
