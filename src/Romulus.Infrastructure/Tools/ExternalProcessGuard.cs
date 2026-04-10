using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Romulus.Infrastructure.Tools;

/// <summary>
/// Tracks externally started child processes and provides deterministic shutdown behavior.
/// On Windows, tracked processes are assigned to a Job Object with KILL_ON_JOB_CLOSE
/// so child processes are not orphaned when the host exits unexpectedly.
/// </summary>
public static class ExternalProcessGuard
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private static readonly ConcurrentDictionary<int, Process> TrackedProcesses = new();
    private static readonly object JobInitGate = new();
    private static nint _jobHandle;
    private static bool _jobInitialized;
    private static bool _jobAvailable;

    private static void EmitDiagnostic(Action<string>? log, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (log is not null)
        {
            try
            {
                log(message);
                return;
            }
            catch (Exception ex)
            {
                message = $"{message} [logger-failed: {ex.GetType().Name}: {ex.Message}]";
            }
        }

        try
        {
            Trace.WriteLine(message);
        }
        catch
        {
            // Never throw from guard diagnostics.
        }
    }

    static ExternalProcessGuard()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAllTrackedProcesses("process-exit");
    }

    /// <summary>
    /// Registers a process for host-lifetime tracking and (on Windows) assigns it to a kill-on-close Job Object.
    /// </summary>
    public static IDisposable Track(Process process, string owner, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (process.HasExited)
            return NoopLease.Instance;

        var pid = process.Id;
        TrackedProcesses[pid] = process;

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += OnTrackedProcessExited;
        }
        catch (InvalidOperationException)
        {
            TrackedProcesses.TryRemove(pid, out _);
            return NoopLease.Instance;
        }

        TryAssignToJobObject(process, owner, log);
        return new TrackingLease(process, pid);
    }

    /// <summary>
    /// Terminates a process tree and waits for shutdown.
    /// </summary>
    public static void TryTerminate(
        Process process,
        string owner,
        TimeSpan? waitTimeout = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(process);

        var timeout = waitTimeout ?? TimeSpan.FromSeconds(5);
        var timeoutMs = timeout.TotalMilliseconds <= 0
            ? 5000
            : (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
        int pid;
        try { pid = process.Id; } catch { pid = -1; }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(timeoutMs))
                    EmitDiagnostic(log, $"{owner}: process {(pid > 0 ? pid : 0)} did not exit within {timeout.TotalSeconds:0.#}s");
            }
        }
        catch (InvalidOperationException)
        {
            // Process exited between checks.
        }
        catch (Win32Exception ex)
        {
            EmitDiagnostic(log, $"{owner}: failed to terminate process {(pid > 0 ? pid : 0)}: {ex.Message}");
        }
        finally
        {
            Detach(process, pid);
        }
    }

    /// <summary>
    /// Terminates all currently tracked processes.
    /// </summary>
    public static void KillAllTrackedProcesses(string owner, Action<string>? log = null)
    {
        foreach (var pair in TrackedProcesses.ToArray())
            TryTerminate(pair.Value, owner, TimeSpan.FromSeconds(2), log);
    }

    internal static int GetTrackedProcessCountForTests() => TrackedProcesses.Count;

    private static void OnTrackedProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
            return;

        int pid;
        try { pid = process.Id; } catch { pid = -1; }
        Detach(process, pid);
    }

    private static void Detach(Process process, int pid)
    {
        try { process.Exited -= OnTrackedProcessExited; }
        catch (InvalidOperationException) { /* process already gone */ }

        if (pid > 0)
            TrackedProcesses.TryRemove(pid, out _);
    }

    private static void TryAssignToJobObject(Process process, string owner, Action<string>? log)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!EnsureJobObject(owner, log))
            return;

        try
        {
            if (!AssignProcessToJobObject(_jobHandle, process.Handle))
            {
                var error = Marshal.GetLastWin32Error();
                // ERROR_ACCESS_DENIED (5): already in a non-breakaway job.
                // ERROR_INVALID_PARAMETER (87): invalid/terminated process.
                if (error is not (5 or 87))
                    EmitDiagnostic(log, $"{owner}: AssignProcessToJobObject failed for pid {process.Id} (win32={error}).");
            }
        }
        catch (InvalidOperationException)
        {
            // Process exited before assignment.
        }
    }

    private static bool EnsureJobObject(string owner, Action<string>? log)
    {
        lock (JobInitGate)
        {
            if (_jobInitialized)
                return _jobAvailable;

            _jobInitialized = true;
            _jobHandle = CreateJobObjectW(nint.Zero, null);
            if (_jobHandle == nint.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                EmitDiagnostic(log, $"{owner}: CreateJobObject failed (win32={error}).");
                _jobAvailable = false;
                return false;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };

            var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, buffer, false);
                if (!SetInformationJobObject(
                        _jobHandle,
                        JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                        buffer,
                        (uint)size))
                {
                    var error = Marshal.GetLastWin32Error();
                    EmitDiagnostic(log, $"{owner}: SetInformationJobObject failed (win32={error}).");
                    CloseHandle(_jobHandle);
                    _jobHandle = nint.Zero;
                    _jobAvailable = false;
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            _jobAvailable = true;
            return true;
        }
    }

    private sealed class TrackingLease(Process process, int pid) : IDisposable
    {
        private Process? _process = process;
        private readonly int _pid = pid;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _process, null);
            if (current is null)
                return;

            Detach(current, _pid);
        }
    }

    private sealed class NoopLease : IDisposable
    {
        public static readonly NoopLease Instance = new();
        public void Dispose() { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private enum JOBOBJECTINFOCLASS
    {
        JobObjectExtendedLimitInformation = 9
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        nint hJob,
        JOBOBJECTINFOCLASS jobObjectInfoClass,
        nint lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);
}
