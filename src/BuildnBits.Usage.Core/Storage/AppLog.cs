using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BuildnBits.Usage.Core.Storage;

public enum AppLogLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Small, best-effort application log. It deliberately has no dependency on a
/// logging framework and redacts provider credential material before writing.
/// </summary>
public sealed class AppLog : IDisposable
{
    public const long DefaultMaxBytes = 512 * 1024;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex SensitiveAssignment = new(
        @"(?i)\b(?:access[_-]?token|api[_-]?key|authorization|bearer|cookie|password|passwd|refresh[_-]?token|secret|token)\b\s*[:=]\s*(?:bearer\s+)?[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Jwt = new(
        @"\beyJ[a-zA-Z0-9_-]{8,}\.[a-zA-Z0-9_-]{8,}\.[a-zA-Z0-9_-]{8,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex KnownSecret = new(
        @"\b(?:sk-[a-zA-Z0-9_-]{12,}|xai-[a-zA-Z0-9_-]{12,}|gh[pousr]_[a-zA-Z0-9_-]{12,}|AIza[a-zA-Z0-9_-]{20,}|ya29\.[a-zA-Z0-9_-]{12,})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _path;
    private readonly string _rotatedPath;
    private readonly long _maxBytes;
    private readonly object _sync = new();
    private bool _disposed;

    public AppLog(string? root = null, long maxBytes = DefaultMaxBytes)
    {
        var dir = StoragePaths.ResolveRoot(root);
        _path = Path.Combine(dir, "app.log");
        _rotatedPath = _path + ".1";
        _maxBytes = maxBytes > 0 ? maxBytes : DefaultMaxBytes;
    }

    public string PathOnDisk => _path;

    public void Info(string message) => Write(AppLogLevel.Info, message);

    public void Warning(string message) => Write(AppLogLevel.Warning, message);

    public void Warn(string message) => Warning(message);

    public void Error(string message) => Write(AppLogLevel.Error, message);

    public void Write(AppLogLevel level, string message)
    {
        var safeMessage = Sanitize(message);
        var levelText = level switch
        {
            AppLogLevel.Info => "INFO",
            AppLogLevel.Warning => "WARN",
            AppLogLevel.Error => "ERROR",
            _ => "INFO"
        };
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'Z'", CultureInfo.InvariantCulture);
        var line = $"{timestamp} {levelText} {safeMessage}{Environment.NewLine}";
        var bytes = Utf8.GetBytes(line);

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(_path) && new FileInfo(_path).Length + bytes.LongLength > _maxBytes)
                {
                    Rotate();
                }

                // A single unusually large message should not defeat the cap.
                if (bytes.LongLength > _maxBytes)
                {
                    var prefix = Utf8.GetBytes(line[..Math.Min(line.Length, 256)]);
                    bytes = prefix[..(int)Math.Min(prefix.Length, _maxBytes)];
                }

                using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Logging must never take down a refresh or the tray host.
            }
        }
    }

    public void EnsureFile()
    {
        lock (_sync)
        {
            if (_disposed || File.Exists(_path))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                using var stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Best effort only.
            }
        }
    }

    public string ReadRecent(int maxLines = 120)
    {
        if (maxLines <= 0)
        {
            return string.Empty;
        }

        lock (_sync)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return string.Empty;
                }

                var lines = File.ReadLines(_path).TakeLast(maxLines);
                return string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return string.Empty;
            }
        }
    }

    public static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "(no details)";
        }

        var safe = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        safe = SensitiveAssignment.Replace(safe, "[redacted]");
        safe = Jwt.Replace(safe, "[redacted-token]");
        safe = KnownSecret.Replace(safe, "[redacted-secret]");

        if (ContainsSensitiveWord(safe))
        {
            return "Sensitive provider information redacted.";
        }

        return safe.Length <= 1000 ? safe : safe[..997] + "...";
    }

    private static bool ContainsSensitiveWord(string value) =>
        value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("auth.json", StringComparison.OrdinalIgnoreCase);

    private void Rotate()
    {
        try
        {
            File.Move(_path, _rotatedPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Preserve the original log operation; a future write can retry.
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }
    }
}
