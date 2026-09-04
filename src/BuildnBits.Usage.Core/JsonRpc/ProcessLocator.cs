namespace BuildnBits.Usage.Core.JsonRpc;

public static class ProcessLocator
{
    public static string? FindOnPath(string fileName)
    {
        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
        {
            return fileName;
        }

        foreach (var candidate in EnumerateCandidates(fileName))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                yield return Path.Combine(dir, AppendExtension(fileName, ext));
            }

            yield return Path.Combine(dir, fileName);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] wellKnown =
        [
            Path.Combine(local, "Programs", "OpenAI", "Codex", "bin"),
            Path.Combine(home, ".grok", "bin"),
            Path.Combine(local, "agy", "bin"),
            Path.Combine(local, "Microsoft", "WinGet", "Links")
        ];
        foreach (var dir in wellKnown)
        {
            yield return Path.Combine(dir, OperatingSystem.IsWindows() ? fileName + ".exe" : fileName);
            yield return Path.Combine(dir, fileName);
        }
    }

    private static string AppendExtension(string fileName, string ext) =>
        fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ext;
}
