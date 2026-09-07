using System.Diagnostics;
using System.Text;

namespace BuildnBits.Usage.Core.JsonRpc;

public static class ProcessLocator
{
    private static readonly string[] DefaultScriptExtensions = [".CMD", ".BAT", ".PS1"];

    public static string? FindOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (Path.IsPathRooted(fileName))
        {
            return FindInDirectory(
                Path.GetDirectoryName(fileName) ?? string.Empty,
                Path.GetFileName(fileName));
        }

        var pathDirectories = GetPathDirectories();
        var allDirectories = pathDirectories.Concat(GetWellKnownDirectories()).ToArray();

        // Search every directory for a real executable before considering a
        // PATHEXT shim. A shim earlier on PATH must not hide a later .exe.
        foreach (var directory in allDirectories)
        {
            foreach (var candidate in EnumerateCandidates(directory, fileName, executableOnly: true))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        foreach (var directory in allDirectories)
        {
            foreach (var candidate in EnumerateCandidates(directory, fileName, executableOnly: false))
            {
                if (IsScriptShim(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a launch target that keeps script shims inside a hidden
    /// console process when no native executable is available.
    /// </summary>
    public static (string FileName, IReadOnlyList<string> Arguments) PrepareLaunch(
        string executable,
        IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsWindows() || !IsScriptShim(executable))
        {
            return (executable, arguments);
        }

        var extension = Path.GetExtension(executable);
        if (extension.Equals(".PS1", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", executable, .. arguments]);
        }

        var command = string.Join(
            " ",
            new[] { QuoteForCommand(executable) }.Concat(arguments.Select(QuoteForCommand)));
        return ("cmd.exe", ["/d", "/s", "/c", "\"" + command + "\""]);
    }

    public static bool IsScriptShim(string path) =>
        Path.GetExtension(path) is { } extension &&
        (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase));

    public static bool UsesCommandShell(string executable) =>
        Path.GetFileName(executable).Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);

    public static string BuildRawArguments(IReadOnlyList<string> arguments) =>
        string.Join(" ", arguments);

    /// <summary>
    /// Builds the common hidden, redirected process configuration used by all
    /// provider CLIs. Keeping this in one place prevents a provider from
    /// accidentally inheriting the tray process's console.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments,
        bool redirectStandardInput)
    {
        var launch = PrepareLaunch(executable, arguments);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var start = new ProcessStartInfo
        {
            FileName = launch.FileName,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            ErrorDialog = false,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8
        };

        if (UsesCommandShell(launch.FileName))
        {
            start.Arguments = BuildRawArguments(launch.Arguments);
        }
        else
        {
            foreach (var argument in launch.Arguments)
            {
                start.ArgumentList.Add(argument);
            }
        }

        return start;
    }

    public static string DescribeLaunch(string executable)
    {
        var kind = IsScriptShim(executable) ? "script-shim" : "native";
        return $"target={Path.GetFileName(executable)} mode={kind}";
    }

    private static string? FindInDirectory(string directory, string fileName)
    {
        foreach (var candidate in EnumerateCandidates(directory, fileName, executableOnly: true))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var candidate in EnumerateCandidates(directory, fileName, executableOnly: false))
        {
            if (IsScriptShim(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(
        string directory,
        string fileName,
        bool executableOnly)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            yield break;
        }

        var leaf = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(leaf))
        {
            yield break;
        }

        var extension = Path.GetExtension(leaf);
        var hasKnownExtension = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                                IsScriptShim(leaf);
        var stem = hasKnownExtension ? Path.GetFileNameWithoutExtension(leaf) : leaf;

        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(directory, stem + ".exe");
        }

        if (!executableOnly && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var scriptExtension in GetScriptExtensions())
            {
                if (hasKnownExtension && !scriptExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return Path.Combine(directory, stem + scriptExtension);
            }
        }

        // A caller may explicitly name a native executable without an .exe
        // suffix. Keep that exact candidate in the native pass so it still
        // wins over a script shim.
        if (!hasKnownExtension && executableOnly)
        {
            yield return Path.Combine(directory, leaf);
        }
    }

    private static IEnumerable<string> GetPathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => directory.Trim().Trim('"'))
            .Where(directory => directory.Length > 0);
    }

    private static IEnumerable<string> GetWellKnownDirectories()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return
        [
            Path.Combine(local, "Programs", "OpenAI", "Codex", "bin"),
            Path.Combine(home, ".grok", "bin"),
            Path.Combine(local, "agy", "bin"),
            Path.Combine(local, "Microsoft", "WinGet", "Links"),
            Path.Combine(roaming, "npm"),
            Path.Combine(programFiles, "nodejs"),
            Path.Combine(programFilesX86, "nodejs")
        ];
    }

    private static IEnumerable<string> GetScriptExtensions()
    {
        var fromPath = (Environment.GetEnvironmentVariable("PATHEXT") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(extension => extension.Trim())
            .Where(extension => extension.Length > 0 &&
                                !extension.Equals(".EXE", StringComparison.OrdinalIgnoreCase));

        return fromPath.Concat(DefaultScriptExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string QuoteForCommand(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
