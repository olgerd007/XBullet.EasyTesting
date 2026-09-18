using System.Diagnostics;

namespace XBullet.EasyTesting.Snapshots;

internal static class SnapshotDiffLauncher
{
    public static bool TryLaunch(
        SnapshotSettings settings,
        string verifiedPath,
        string receivedPath)
    {
        if (!settings.LaunchDiffTool || ContinuousIntegrationEnvironment.IsDetected())
        {
            return false;
        }

        var tool = settings.DiffTool ?? Discover();
        if (tool is null)
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = tool.Executable,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(verifiedPath) ?? Environment.CurrentDirectory
            };

            foreach (var argument in tool.Arguments)
            {
                startInfo.ArgumentList.Add(
                    argument
                        .Replace("{verified}", verifiedPath, StringComparison.Ordinal)
                        .Replace("{received}", receivedPath, StringComparison.Ordinal));
            }

            return Process.Start(startInfo) is not null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static SnapshotDiffTool? Discover()
    {
        if (OperatingSystem.IsWindows())
        {
            var visualStudio = FindVisualStudio();
            if (visualStudio is not null)
            {
                return SnapshotDiffTool.VisualStudio(visualStudio);
            }
        }

        var code = FindOnPath(OperatingSystem.IsWindows() ? "code.cmd" : "code");
        if (code is not null)
        {
            return SnapshotDiffTool.VisualStudioCode(code);
        }

        var rider = FindOnPath(OperatingSystem.IsWindows() ? "rider64.exe" : "rider");
        if (rider is not null)
        {
            return SnapshotDiffTool.Rider(rider);
        }

        var meld = FindOnPath(OperatingSystem.IsWindows() ? "meld.exe" : "meld");
        return meld is null
            ? null
            : new SnapshotDiffTool(meld, "{verified}", "{received}");
    }

    private static string? FindVisualStudio()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vsWhere = Path.Combine(
            programFiles,
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");
        if (!File.Exists(vsWhere))
        {
            return FindOnPath("devenv.exe");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = vsWhere,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-latest");
            startInfo.ArgumentList.Add("-products");
            startInfo.ArgumentList.Add("*");
            startInfo.ArgumentList.Add("-find");
            startInfo.ArgumentList.Add(@"Common7\IDE\devenv.exe");

            using var process = Process.Start(startInfo);
            if (process is null || !process.WaitForExit(milliseconds: 2_000))
            {
                return null;
            }

            var result = process.StandardOutput.ReadToEnd().Trim();
            return File.Exists(result) ? result : null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? FindOnPath(string executable)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Ignore malformed PATH entries and continue discovery.
            }
        }

        return null;
    }

}
