using System.Diagnostics;
using System.IO;
using System.Text;

namespace OrbotPc.Core;

/// <summary>Supervises the optional, capacity-limited Snowflake volunteer relay.</summary>
public sealed class SnowflakeVolunteerRuntime : IAsyncDisposable
{
    public const uint DefaultCapacity = 1;

    private readonly string _executablePath;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Process? _process;

    public event Action<string>? LogMessage;
    public event Action<int>? Exited;
    public bool IsRunning => _process is { HasExited: false };

    public SnowflakeVolunteerRuntime(string basePath, string? executablePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        _executablePath = Path.GetFullPath(executablePath ?? Path.Combine(basePath, "runtime", "snowflake", "snowflake-proxy.exe"));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning) return;
            if (!File.Exists(_executablePath))
                throw new FileNotFoundException("The bundled Snowflake volunteer relay is missing.", _executablePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                WorkingDirectory = Path.GetDirectoryName(_executablePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in BuildArguments()) startInfo.ArgumentList.Add(argument);

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => ForwardLog(e.Data);
            process.ErrorDataReceived += (_, e) => ForwardLog(e.Data);
            process.Exited += (_, _) => _ = HandleUnexpectedExitAsync(process);
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("The Snowflake volunteer relay could not start.");
            }

            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                _process = null;
                process.Dispose();
                throw new InvalidOperationException($"The Snowflake volunteer relay exited during startup (code {exitCode}).");
            }
            LogMessage?.Invoke($"Snowflake volunteer relay started with capacity {DefaultCapacity}.");
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            var process = _process;
            _process = null;
            if (process is null) return;
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) { LogMessage?.Invoke("Snowflake relay shutdown: " + ex.Message); }
            process.Dispose();
            LogMessage?.Invoke("Snowflake volunteer relay stopped.");
        }
        finally { _lifecycle.Release(); }
    }

    public static IReadOnlyList<string> BuildArguments(uint capacity = DefaultCapacity)
    {
        if (capacity is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(capacity));
        return ["-capacity", capacity.ToString(System.Globalization.CultureInfo.InvariantCulture), "-disable-stats-logger", "-summary-interval", "0s"];
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_executablePath))
            throw new FileNotFoundException("The bundled Snowflake volunteer relay is missing.", _executablePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            WorkingDirectory = Path.GetDirectoryName(_executablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-version");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not inspect the Snowflake relay binary.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout) + (await stderr);
        if (process.ExitCode != 0 || !output.Contains("snowflake-proxy", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The bundled Snowflake relay did not return a valid version.");
        return output.Trim();
    }

    private void ForwardLog(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line)) LogMessage?.Invoke(line);
    }

    private async Task HandleUnexpectedExitAsync(Process process)
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (!ReferenceEquals(_process, process)) return;
            _process = null;
            var exitCode = process.ExitCode;
            process.Dispose();
            LogMessage?.Invoke($"Snowflake volunteer relay exited (code {exitCode}).");
            Exited?.Invoke(exitCode);
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
