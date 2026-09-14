using XBullet.EasyTesting.Snapshots;
using Xunit;

namespace XBullet.EasyTesting.Tests;

public sealed class SnapshotAssertTests
{
    [Fact]
    public async Task New_snapshot_writes_a_received_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("new-snapshot")
            .Updating(SnapshotUpdateMode.None)
            .WithoutDiffTool();

        try
        {
            var exception = await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken));

            Assert.True(File.Exists(exception.ReceivedPath));
            Assert.False(File.Exists(exception.VerifiedPath));

            var verifiedPath = SnapshotAssert.AcceptReceived(exception.ReceivedPath);

            Assert.Equal(exception.VerifiedPath, verifiedPath);
            Assert.True(File.Exists(verifiedPath));
            await SnapshotAssert.MatchAsync(new { Value = 42 }, settings, cancellationToken);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public void Controller_snapshot_options_support_fluent_capture_configuration()
    {
        var options = new ControllerSnapshotOptions()
            .WithoutRequest()
            .WithoutBody()
            .IgnoringHeaders("ETag", "X-Correlation-ID")
            .IncludingHeader("Date");

        Assert.False(options.IncludeRequest);
        Assert.False(options.IncludeBody);
        Assert.Contains("ETag", options.IgnoredHeaders);
        Assert.DoesNotContain("Date", options.IgnoredHeaders);
    }

    [Fact]
    public async Task Automatic_update_modes_create_and_replace_verified_snapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings
        {
            Directory = snapshotDirectory,
            SnapshotName = "automatic-update",
            UpdateMode = SnapshotUpdateMode.Missing,
            LaunchDiffTool = false
        };

        try
        {
            await SnapshotAssert.MatchAsync(new { Version = 1 }, settings, cancellationToken);

            settings.UpdateMode = SnapshotUpdateMode.None;
            await Assert.ThrowsAsync<SnapshotMismatchException>(() =>
                SnapshotAssert.MatchAsync(new { Version = 2 }, settings, cancellationToken));

            settings.UpdateMode = SnapshotUpdateMode.All;
            await SnapshotAssert.MatchAsync(new { Version = 2 }, settings, cancellationToken);

            settings.UpdateMode = SnapshotUpdateMode.None;
            await SnapshotAssert.MatchAsync(new { Version = 2 }, settings, cancellationToken);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    [Fact]
    public async Task Structured_scrubbing_stabilizes_dynamic_values_and_ignored_members()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDirectory = CreateTemporarySnapshotDirectory();
        var settings = new SnapshotSettings()
            .InDirectory(snapshotDirectory)
            .Named("structured-scrubbing")
            .ScrubMembers("RequestId", "CreatedAt")
            .IgnoreMembers("AccessToken")
            .ScrubGuids()
            .ScrubDateTimes()
            .Updating(SnapshotUpdateMode.Missing)
            .WithoutDiffTool();

        try
        {
            await SnapshotAssert.MatchAsync(
                new
                {
                    RequestId = 123,
                    CreatedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
                    AccessToken = "secret-one",
                    Metadata = new
                    {
                        CorrelationId = Guid.Parse("3c91271a-a927-4516-b225-41cce45963b5"),
                        ExpiresAt = "2026-01-01T11:00:00Z"
                    }
                },
                settings,
                cancellationToken);

            settings.Updating(SnapshotUpdateMode.None);
            await SnapshotAssert.MatchAsync(
                new
                {
                    RequestId = 999,
                    CreatedAt = new DateTimeOffset(2030, 2, 2, 12, 0, 0, TimeSpan.Zero),
                    AccessToken = "secret-two",
                    Metadata = new
                    {
                        CorrelationId = Guid.Parse("637717e4-10d3-4302-bff2-846b17f00591"),
                        ExpiresAt = "2030-02-02T13:00:00Z"
                    }
                },
                settings,
                cancellationToken);

            var verifiedPath = Directory.EnumerateFiles(snapshotDirectory, "*.verified.json").Single();
            var verified = await File.ReadAllTextAsync(verifiedPath, cancellationToken);

            Assert.Contains("\"RequestId\": \"{Scrubbed}\"", verified);
            Assert.Contains("\"CorrelationId\": \"{Guid}\"", verified);
            Assert.Contains("\"ExpiresAt\": \"{DateTime}\"", verified);
            Assert.DoesNotContain("AccessToken", verified);
            Assert.DoesNotContain("secret-one", verified);
        }
        finally
        {
            DeleteTemporarySnapshotDirectory(snapshotDirectory);
        }
    }

    private static string CreateTemporarySnapshotDirectory() =>
        Path.Combine(
            Path.GetTempPath(),
            "XBullet.EasyTesting.Tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteTemporarySnapshotDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
