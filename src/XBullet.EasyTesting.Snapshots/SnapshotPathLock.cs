using System.Security.Cryptography;
using System.Text;

namespace XBullet.EasyTesting.Snapshots;

internal static class SnapshotPathLock
{
    private const int RetryDelayMilliseconds = 25;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static IDisposable Acquire(string path)
    {
        var entry = AddReference(path);
        var semaphoreAcquired = false;
        try
        {
            entry.Semaphore.Wait();
            semaphoreAcquired = true;
            var processLock = AcquireProcessLock(path);
            return new Releaser(path, entry, processLock);
        }
        catch
        {
            if (semaphoreAcquired)
            {
                entry.Semaphore.Release();
            }

            RemoveReference(path, entry);
            throw;
        }
    }

    public static async ValueTask<IDisposable> AcquireAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var entry = AddReference(path);
        var semaphoreAcquired = false;
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            semaphoreAcquired = true;
            var processLock = await AcquireProcessLockAsync(path, cancellationToken);
            return new Releaser(path, entry, processLock);
        }
        catch
        {
            if (semaphoreAcquired)
            {
                entry.Semaphore.Release();
            }

            RemoveReference(path, entry);
            throw;
        }
    }

    private static Entry AddReference(string path)
    {
        lock (Sync)
        {
            if (!Entries.TryGetValue(path, out var entry))
            {
                entry = new Entry();
                Entries.Add(path, entry);
            }

            entry.ReferenceCount++;
            return entry;
        }
    }

    private static void RemoveReference(string path, Entry entry)
    {
        lock (Sync)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                Entries.Remove(path);
                entry.Semaphore.Dispose();
            }
        }
    }

    private static FileStream AcquireProcessLock(string path)
    {
        var lockPath = GetProcessLockPath(path);
        while (true)
        {
            try
            {
                return OpenProcessLock(lockPath);
            }
            catch (IOException)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
    }

    private static async Task<FileStream> AcquireProcessLockAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var lockPath = GetProcessLockPath(path);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return OpenProcessLock(lockPath);
            }
            catch (IOException)
            {
                await Task.Delay(RetryDelayMilliseconds, cancellationToken);
            }
        }
    }

    private static string GetProcessLockPath(string path)
    {
        var directory = Path.Combine(Path.GetTempPath(), "XBullet.EasyTesting.Snapshots", "locks");
        System.IO.Directory.CreateDirectory(directory);
        var normalizedPath = Path.GetFullPath(path).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        return Path.Combine(directory, $"{hash}.lock");
    }

    private static FileStream OpenProcessLock(string lockPath) =>
        new(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.None);

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(initialCount: 1, maxCount: 1);

        public int ReferenceCount { get; set; }
    }

    private sealed class Releaser(string path, Entry entry, FileStream processLock) : IDisposable
    {
        private Entry? _entry = entry;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _entry, null);
            if (current is null)
            {
                return;
            }

            try
            {
                processLock.Dispose();
            }
            finally
            {
                current.Semaphore.Release();
                RemoveReference(path, current);
            }
        }
    }
}
