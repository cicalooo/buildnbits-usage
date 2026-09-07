using System.Diagnostics;
using BuildnBits.Usage.Core.JsonRpc;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Providers;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Core.Providers.Agy;

public sealed record AgyCommandResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class AgyUsageClient : IUsageProvider
{
    public static readonly TimeSpan DefaultUsageTimeout = TimeSpan.FromSeconds(15);

    private static readonly IReadOnlyList<string> UsageArguments =
        ["-p", "/usage", "--output-format", "json"];

    private readonly string _executableName;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<AgyCommandResult>> _commandRunner;
    private readonly AppLog? _log;
    private readonly TimeSpan _timeout;

    public AgyUsageClient(
        string executableName = "agy",
        Func<string, IReadOnlyList<string>, CancellationToken, Task<AgyCommandResult>>? commandRunner = null,
        AppLog? appLog = null,
        TimeSpan? timeout = null)
    {
        _executableName = executableName;
        _log = appLog;
        _timeout = timeout.HasValue && timeout.Value > TimeSpan.Zero ? timeout.Value : DefaultUsageTimeout;
        _commandRunner = commandRunner ?? RunProcessAsync;
    }

    public async Task<ProviderSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var exe = ProcessLocator.FindOnPath(_executableName);
        if (exe is null)
        {
            return new ProviderSnapshot(
                ProviderKind.Agy,
                UsageStatus.MissingCli,
                null,
                [],
                DateTimeOffset.UtcNow,
                "agy executable was not found on PATH.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        AgyCommandResult command;
        try
        {
            command = await _commandRunner(exe, UsageArguments, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Error("Antigravity usage command timed out.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }

        if (command.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(command.StandardError)
                ? $"agy usage command exited with code {command.ExitCode}."
                : command.StandardError.Trim();
            return Failure(message);
        }

        try
        {
            return AgyUsageParser.Parse(command.StandardOutput, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            return Failure(ex.Message);
        }
    }

    private async Task<AgyCommandResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var launch = ProcessLocator.PrepareLaunch(executable, arguments);
        var start = ProcessLocator.CreateStartInfo(executable, arguments, redirectStandardInput: false);

        using var process = new Process { StartInfo = start };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start agy.");
            }

        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start agy: {ex.Message}", ex);
        }

        using var job = WindowsProcessJob.Create();
        var jobAssigned = job?.TryAssign(process) == true;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        _log?.Info($"agy process started: {ProcessLocator.DescribeLaunch(executable)} " +
                   $"launcher={Path.GetFileName(launch.FileName)} pid={process.Id} " +
                   $"job={(jobAssigned ? "assigned" : "unavailable")}.");
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var result = new AgyCommandResult(
                process.ExitCode,
                await stdout.ConfigureAwait(false),
                await stderr.ConfigureAwait(false));
            _log?.Info($"agy process exited: pid={process.Id} code={result.ExitCode} duration={stopwatch.Elapsed.TotalSeconds:0.###}s.");
            return result;
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may have exited between HasExited and Kill.
            }

            _log?.Info($"agy process cleanup completed: duration={stopwatch.Elapsed.TotalSeconds:0.###}s.");
        }
    }

    private static ProviderSnapshot Failure(string message)
    {
        if (IsAuthenticationError(message))
        {
            return new ProviderSnapshot(
                ProviderKind.Agy,
                UsageStatus.Unauthenticated,
                null,
                [],
                DateTimeOffset.UtcNow,
                "Antigravity authentication is required. Run agy once to sign in; this app never reads credentials.");
        }

        return Error(message);
    }

    private static ProviderSnapshot Error(string message) =>
        new(ProviderKind.Agy, UsageStatus.Error, null, [], DateTimeOffset.UtcNow, Sanitize(message));

    private static bool IsAuthenticationError(string message) =>
        message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("authenticate", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("login", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("oauth", StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string message)
    {
        if (message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return "Antigravity request failed.";
        }

        return string.IsNullOrWhiteSpace(message) ? "Antigravity request failed." : message;
    }
}

public sealed class AntigravityUsageClient : IUsageProvider
{
    private readonly AgyUsageClient _inner;

    public AntigravityUsageClient(
        string executableName = "agy",
        Func<string, IReadOnlyList<string>, CancellationToken, Task<AgyCommandResult>>? commandRunner = null,
        AppLog? appLog = null,
        TimeSpan? timeout = null)
    {
        _inner = new AgyUsageClient(executableName, commandRunner, appLog, timeout);
    }

    public Task<ProviderSnapshot> FetchAsync(CancellationToken cancellationToken) =>
        _inner.FetchAsync(cancellationToken);
}
