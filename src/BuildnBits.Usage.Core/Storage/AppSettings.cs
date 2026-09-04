using System.Text.Json;

namespace BuildnBits.Usage.Core.Storage;

public sealed class AppSettings
{
    public bool ShowCodexIcon { get; set; } = true;
    public bool ShowGrokIcon { get; set; } = true;
    public bool ShowAgyIcon { get; set; } = true;
    public bool LargerTrayDigits { get; set; } = true;
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly object _sync = new();

    public AppSettingsStore(string? root = null)
    {
        var dir = StoragePaths.ResolveRoot(root);
        _path = Path.Combine(dir, "settings.json");
    }

    public string PathOnDisk => _path;

    public AppSettings Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        if (!settings.ShowCodexIcon && !settings.ShowGrokIcon && !settings.ShowAgyIcon)
        {
            settings.ShowCodexIcon = true;
        }

        lock (_sync)
        {
            var tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(tmp, _path, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp))
                    {
                        File.Delete(tmp);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Preserve the original save exception, if any.
                }
            }
        }
    }
}
