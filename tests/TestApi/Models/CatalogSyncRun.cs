namespace TestApi.Models;

public sealed class CatalogSyncRun
{
    public long Id { get; set; }

    public required string Category { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public CatalogSyncStatus Status { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public string? FailureReason { get; set; }
}

public enum CatalogSyncStatus
{
    Running,
    Completed,
    Failed
}
