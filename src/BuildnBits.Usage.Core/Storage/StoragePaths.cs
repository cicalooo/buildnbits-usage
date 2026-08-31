namespace BuildnBits.Usage.Core.Storage;

public static class StoragePaths
{
    public const string PortableMarkerFileName = "portable.flag";

    public static string ResolveRoot(string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            Directory.CreateDirectory(explicitRoot);
            return explicitRoot;
        }

        var baseDir = AppContext.BaseDirectory;
        var marker = Path.Combine(baseDir, PortableMarkerFileName);
        var portableEnv = Environment.GetEnvironmentVariable("BUILDNBITS_USAGE_PORTABLE");
        var portable = File.Exists(marker) ||
                       string.Equals(portableEnv, "1", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(portableEnv, "true", StringComparison.OrdinalIgnoreCase);
        var dir = portable
            ? Path.Combine(baseDir, "data")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BuildnBits",
                "Usage");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
