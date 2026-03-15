using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Tools;

/// <summary>
/// Production implementation of IToolRunner.
/// Port of Tools.ps1 — tool discovery, hash verification, process execution.
/// </summary>
public sealed class ToolRunnerAdapter : IToolRunner
{
    private readonly string? _toolHashesPath;
    private readonly bool _allowInsecureHashBypass;
    private readonly int _timeoutMinutes;
    private Dictionary<string, string>? _toolHashes;
    private readonly object _toolHashLock = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Hash, DateTime LastWriteUtc, long Length)> _hashCache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="toolHashesPath">Path to data/tool-hashes.json for SHA256 verification.</param>
    /// <param name="allowInsecureHashBypass">Skip hash check (NOT recommended for production).</param>
    public ToolRunnerAdapter(string? toolHashesPath = null, bool allowInsecureHashBypass = false, int timeoutMinutes = 30, Action<string>? log = null)
    {
        _toolHashesPath = toolHashesPath;
        _allowInsecureHashBypass = allowInsecureHashBypass;
        _timeoutMinutes = timeoutMinutes > 0 ? timeoutMinutes : 30;
        _log = log;
    }

    private readonly Action<string>? _log;

    public string? FindTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        var name = toolName.ToLowerInvariant();

        // 1. Search known safe locations first (never user-writable) to prevent
        //    tool-hijacking via PATH poisoning with a rogue executable.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // Build candidate paths using environment-based folders (no hardcoded drive letters)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = name switch
        {
            "chdman" => new[]
            {
                Path.Combine(programFiles, "MAME", "chdman.exe"),
                Path.Combine(programFilesX86, "MAME", "chdman.exe"),
                Path.Combine(localAppData, "MAME", "chdman.exe")
            },
            "dolphintool" => new[]
            {
                Path.Combine(programFiles, "Dolphin", "DolphinTool.exe"),
                Path.Combine(programFilesX86, "Dolphin", "DolphinTool.exe")
            },
            "7z" => new[]
            {
                Path.Combine(programFiles, "7-Zip", "7z.exe"),
                Path.Combine(programFilesX86, "7-Zip", "7z.exe")
            },
            "psxtract" => new[]
            {
                Path.Combine(localAppData, "RomCleanup", "tools", "psxtract.exe"),
                Path.Combine(programFiles, "psxtract", "psxtract.exe")
            },
            "ciso" => new[]
            {
                Path.Combine(localAppData, "RomCleanup", "tools", "ciso.exe"),
                Path.Combine(programFiles, "ciso", "ciso.exe")
            },
            _ => Array.Empty<string>()
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        // 2. Fallback: check PATH (lower priority — user-writable dirs may be in PATH)
        var pathResult = FindOnPath(name + ".exe");
        if (pathResult is not null)
            return pathResult;

        return null;
    }

    public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
    {
        var label = errorLabel ?? "Tool";

        if (!File.Exists(filePath))
            return new ToolResult(-1, $"{label}: executable not found at '{filePath}'", false);

        if (!VerifyToolHash(filePath))
            return new ToolResult(-1, $"{label}: hash verification failed for '{filePath}'", false);

        return RunProcess(filePath, arguments, label);
    }

    public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
    {
        if (!File.Exists(sevenZipPath))
            return new ToolResult(-1, "7z: executable not found", false);

        if (!VerifyToolHash(sevenZipPath))
            return new ToolResult(-1, "7z: hash verification failed", false);

        return RunProcess(sevenZipPath, arguments, "7z");
    }

    private ToolResult RunProcess(string exePath, string[] arguments, string label)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            // Use ArgumentList instead of Arguments string to prevent argument injection
            // via filenames containing quotes, spaces, or shell metacharacters.
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null)
                return new ToolResult(-1, $"{label}: failed to start process", false);

            // Read both stdout and stderr asynchronously to prevent pipe deadlock.
            // If either pipe fills its 4KB OS buffer while the parent blocks reading the other,
            // both processes deadlock indefinitely.
            string? stderr = null;
            string? stdout = null;
            var stderrTask = Task.Run(() => stderr = process.StandardError.ReadToEnd());
            var stdoutTask = Task.Run(() => stdout = process.StandardOutput.ReadToEnd());

            var completed = Task.WaitAll(new[] { stdoutTask, stderrTask },
                TimeSpan.FromMinutes(_timeoutMinutes));

            if (!completed)
            {
                if (!process.HasExited)
                    try { process.Kill(entireProcessTree: true); } catch { }
                return new ToolResult(-1, $"{label}: process timed out after {_timeoutMinutes} minutes", false);
            }

            process.WaitForExit();

            var output = string.IsNullOrEmpty(stderr) ? stdout ?? "" : $"{stdout}\n{stderr}".Trim();
            var success = process.ExitCode == 0;

            return new ToolResult(process.ExitCode, output, success);
        }
        catch (Exception ex)
        {
            return new ToolResult(-1, $"{label}: {ex.Message}", false);
        }
    }

    private bool VerifyToolHash(string toolPath)
    {
        if (_allowInsecureHashBypass)
            return true;

        if (_toolHashesPath is null || !File.Exists(_toolHashesPath))
        {
            _log?.Invoke(
                $"[SECURITY] tool-hashes.json nicht gefunden — blockiere Tool-Ausfuehrung fuer {Path.GetFileName(toolPath)}. " +
                "Lege die Datei data/tool-hashes.json mit den erwarteten SHA256-Checksummen der externen Tools an (siehe Repository-Dokumentation/CI-Artefakte), " +
                "oder aktiviere explizit den unsicheren Bypass (allowInsecureHashBypass / AllowInsecureToolHashBypass) NUR fuer lokale Entwicklungs-Workflows.");
            return false;
        }

        EnsureToolHashesLoaded();

        var fileName = Path.GetFileName(toolPath).ToLowerInvariant();

        if (_toolHashes is null || !_toolHashes.TryGetValue(fileName, out var expectedHash))
        {
            _log?.Invoke($"[SECURITY] Kein erwarteter Hash fuer {fileName} gefunden — blockiere Tool-Ausfuehrung");
            return false;
        }

        // Issue #11: Reject PLACEHOLDER hashes — these are not real SHA256 checksums
        if (expectedHash.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            _log?.Invoke($"[SECURITY] PLACEHOLDER-Hash fuer {fileName} — ersetze durch echten SHA256-Hash in data/tool-hashes.json");
            return false;
        }

        // PERF-02: Cache tool hash with LastWriteTime + FileLength check (Issue #22)
        var fullPath = Path.GetFullPath(toolPath);
        var fileInfo = new FileInfo(fullPath);
        var lastWrite = fileInfo.LastWriteTimeUtc;
        var fileLength = fileInfo.Length;
        if (_hashCache.TryGetValue(fullPath, out var cached)
            && cached.LastWriteUtc == lastWrite
            && cached.Length == fileLength)
        {
            return string.Equals(cached.Hash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(toolPath);
        var hashBytes = sha256.ComputeHash(stream);
        var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        _hashCache[fullPath] = (actualHash, lastWrite, fileLength);

        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureToolHashesLoaded()
    {
        if (_toolHashes is not null || _toolHashesPath is null)
            return;

        lock (_toolHashLock)
        {
            if (_toolHashes is not null)
                return;

            try
            {
                var json = File.ReadAllText(_toolHashesPath);
                using var doc = JsonDocument.Parse(json);

                var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (doc.RootElement.TryGetProperty("Tools", out var toolsElement))
                {
                    foreach (var prop in toolsElement.EnumerateObject())
                    {
                        var value = prop.Value.GetString();
                        if (value is not null)
                            hashes[prop.Name.ToLowerInvariant()] = value;
                    }
                }

                _toolHashes = hashes;
            }
            catch
            {
                _toolHashes = new Dictionary<string, string>();
            }
        }
    }

    private static string? FindOnPath(string exeName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            return null;

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            var fullPath = Path.Combine(dir.Trim(), exeName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }
}
