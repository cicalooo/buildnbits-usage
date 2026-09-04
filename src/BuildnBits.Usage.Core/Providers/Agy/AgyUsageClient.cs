using System.Diagnostics;
using System.Text;
using BuildnBits.Usage.Core.JsonRpc;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Providers;

namespace BuildnBits.Usage.Core.Providers.Agy;

public sealed record AgyCommandResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class AgyUsageClient : IUsageProvider
{
    private static readonly IReadOnlyList<string> UsageArguments =
        ["-p", "/usage", "--output-format", "json"];

    private readonly string _executableName;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<AgyCommandResult>> _commandRunner;

    public AgyUsageClient(
        string executableName = "agy",
        Func<string, IReadOnlyList<string>, CancellationToken, Task<AgyCommandResult>>? commandRunner = null)
    {
        _executableName = executableName;
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
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

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

    private static async Task<AgyCommandResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
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

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new AgyCommandResult(
                process.ExitCode,
                await stdout.ConfigureAwait(false),
                await stderr.ConfigureAwait(false));
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
        Func<string, IReadOnlyList<string>, CancellationToken, Task<AgyCommandResult>>? commandRunner = null)
    {
        _inner = new AgyUsageClient(executableName, commandRunner);
    }

    public Task<ProviderSnapshot> FetchAsync(CancellationToken cancellationToken) =>
        _inner.FetchAsync(cancellationToken);
}
