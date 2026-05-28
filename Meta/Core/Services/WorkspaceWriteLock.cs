using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Meta.Core.Services;

internal static class WorkspaceWriteLock
{
    private const string LockFileName = ".meta.lock";
    private const int MaxAcquireAttempts = 3;

    public static WorkspaceWriteLockHandle Acquire(string workspaceRootPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            throw new ArgumentException("Workspace root path is required.", nameof(workspaceRootPath));
        }

        var root = Path.GetFullPath(workspaceRootPath);
        Directory.CreateDirectory(root);
        var lockPath = Path.Combine(root, LockFileName);

        for (var attempt = 0; attempt < MaxAcquireAttempts; attempt++)
        {
            var record = WorkspaceLockRecord.CreateCurrent();
            try
            {
                var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                var content = record.Serialize();
                using (var writer = new StreamWriter(stream, leaveOpen: true))
                {
                    writer.Write(content);
                    writer.Flush();
                }

                stream.Position = 0;
                return new WorkspaceWriteLockHandle(lockPath, stream);
            }
            catch (IOException) when (File.Exists(lockPath))
            {
                if (TryReadLockRecord(lockPath, out var existingRecord) &&
                    existingRecord != null &&
                    IsStale(existingRecord))
                {
                    TryDeleteLockFile(lockPath);
                    continue;
                }

                throw BuildActiveLockException(lockPath, existingRecord);
            }
        }

        throw new InvalidOperationException($"Failed to acquire workspace lock '{lockPath}'.");
    }

    private static bool TryReadLockRecord(string lockPath, out WorkspaceLockRecord? record)
    {
        record = null;
        try
        {
            var content = File.ReadAllText(lockPath);
            if (!WorkspaceLockRecord.TryParse(content, out var parsed))
            {
                return false;
            }

            record = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsStale(WorkspaceLockRecord record)
    {
        if (record == null)
        {
            return true;
        }

        if (record.Pid <= 0)
        {
            return true;
        }

        if (!string.Equals(record.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(record.Pid);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch
        {
            return false;
        }

        try
        {
            var actualStartTimeUtc = process.StartTime.ToUniversalTime();
            if (record.ProcessStartTimeUtc.HasValue &&
                actualStartTimeUtc != record.ProcessStartTimeUtc.Value)
            {
                return true;
            }
        }
        catch
        {
            // If process is alive but start time cannot be read, assume lock is active.
            return false;
        }

        return false;
    }

    private static void TryDeleteLockFile(string lockPath)
    {
        try
        {
            File.Delete(lockPath);
        }
        catch
        {
            // Ignore. If lock is active, deletion should fail on Windows.
        }
    }

    private static InvalidOperationException BuildActiveLockException(
        string lockPath,
        WorkspaceLockRecord? record)
    {
        if (record == null)
        {
            return new InvalidOperationException(
                $"Workspace is locked by another process. Lock file: '{lockPath}'.");
        }

        var acquired = record.AcquiredUtc?.ToString("o") ?? "unknown";
        return new InvalidOperationException(
            $"Workspace is locked. lockFile='{lockPath}', pid={record.Pid}, machine='{record.MachineName}', acquiredUtc='{acquired}'.");
    }
}

internal sealed class WorkspaceWriteLockHandle : IDisposable
{
    private readonly FileStream stream;
    private bool disposed;

    public WorkspaceWriteLockHandle(string lockPath, FileStream stream)
    {
        LockPath = lockPath ?? throw new ArgumentNullException(nameof(lockPath));
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public string LockPath { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            stream.Dispose();
        }
        finally
        {
            try
            {
                File.Delete(LockPath);
            }
            catch
            {
                // Ignore best-effort cleanup.
            }
        }
    }
}

internal sealed class WorkspaceLockRecord
{
    public int Pid { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string ToolVersion { get; set; } = string.Empty;
    public DateTime? ProcessStartTimeUtc { get; set; }
    public DateTime? AcquiredUtc { get; set; }

    public static WorkspaceLockRecord CreateCurrent()
    {
        DateTime? processStart = null;
        try
        {
            processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch
        {
            // ignore
        }

        var toolVersion = typeof(WorkspaceService).Assembly.GetName().Version?.ToString() ?? "unknown";
        return new WorkspaceLockRecord
        {
            Pid = Environment.ProcessId,
            MachineName = Environment.MachineName,
            ToolVersion = toolVersion,
            ProcessStartTimeUtc = processStart,
            AcquiredUtc = DateTime.UtcNow,
        };
    }

    public string Serialize()
    {
        return string.Join(
                   Environment.NewLine,
                   new[]
                   {
                       $"Pid={Pid.ToString(CultureInfo.InvariantCulture)}",
                       $"MachineName={MachineName ?? string.Empty}",
                       $"ToolVersion={ToolVersion ?? string.Empty}",
                       $"ProcessStartTimeUtc={ProcessStartTimeUtc?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty}",
                       $"AcquiredUtc={AcquiredUtc?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty}",
                   }) +
               Environment.NewLine;
    }

    public static bool TryParse(string content, out WorkspaceLockRecord record)
    {
        record = null!;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                map[key] = value;
            }
        }

        if (!map.TryGetValue("Pid", out var pidText) ||
            !int.TryParse(pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
        {
            return false;
        }

        map.TryGetValue("MachineName", out var machineName);
        map.TryGetValue("ToolVersion", out var toolVersion);
        map.TryGetValue("ProcessStartTimeUtc", out var processStartText);
        map.TryGetValue("AcquiredUtc", out var acquiredText);

        var hasProcessStart = DateTime.TryParseExact(
            processStartText,
            "o",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var processStartTimeUtc);
        var hasAcquired = DateTime.TryParseExact(
            acquiredText,
            "o",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var acquiredUtc);

        record = new WorkspaceLockRecord
        {
            Pid = pid,
            MachineName = machineName ?? string.Empty,
            ToolVersion = toolVersion ?? string.Empty,
            ProcessStartTimeUtc = hasProcessStart ? processStartTimeUtc : null,
            AcquiredUtc = hasAcquired ? acquiredUtc : null,
        };

        return true;
    }
}
