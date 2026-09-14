using System.Text.Json;
using BuildnBits.Usage.Core.Models;

namespace BuildnBits.Usage.Core.Storage;

public sealed class AppSettings
{
    public const int DefaultRefreshIntervalMinutes = 5;
    public static readonly IReadOnlyList<int> AllowedRefreshIntervalMinutes = [3, 5, 10];
    public static IReadOnlyList<int> AllowedRefreshIntervals => AllowedRefreshIntervalMinutes;

    public bool ShowCodexIcon { get; set; } = true;
    public bool ShowGrokIcon { get; set; } = true;
    public bool ShowAgyIcon { get; set; } = true;
    public Dictionary<string, bool> TraySquareVisibility { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public bool LargerTrayDigits { get; set; } = true;

    private int _refreshIntervalMinutes = DefaultRefreshIntervalMinutes;

    public int RefreshIntervalMinutes
    {
        get => _refreshIntervalMinutes;
        set => _refreshIntervalMinutes = ClampRefreshIntervalMinutes(value);
    }

    public static int ClampRefreshIntervalMinutes(int minutes) =>
        AllowedRefreshIntervalMinutes.Contains(minutes) ? minutes : DefaultRefreshIntervalMinutes;

    public static int ClampRefreshInterval(int minutes) =>
        ClampRefreshIntervalMinutes(minutes);

    public bool IsTraySquareVisible(string key, ProviderKind provider)
    {
        if (TraySquareVisibility is not null && TraySquareVisibility.TryGetValue(key, out var visible))
        {
            return visible;
        }

        return provider switch
        {
            ProviderKind.Codex => ShowCodexIcon,
            ProviderKind.Grok => ShowGrokIcon,
            ProviderKind.Agy => ShowAgyIcon,
            _ => true
        };
    }

    public void SetTraySquareVisible(string key, bool visible)
    {
        TraySquareVisibility ??= new(StringComparer.OrdinalIgnoreCase);
        TraySquareVisibility[key] = visible;
    }

    public void Normalize()
    {
        RefreshIntervalMinutes = RefreshIntervalMinutes;
        var visibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (TraySquareVisibility is not null)
        {
            foreach (var pair in TraySquareVisibility)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    visibility[pair.Key] = pair.Value;
                }
            }
        }

        TraySquareVisibility = visibility;
        var seedLegacyVisibility = TraySquareVisibility.Count == 0;
        if (seedLegacyVisibility)
        {
            TraySquareVisibility[TraySquareKeys.CodexFiveHour] = ShowCodexIcon;
            TraySquareVisibility[TraySquareKeys.CodexSevenDay] = ShowCodexIcon;
            TraySquareVisibility[TraySquareKeys.GrokWeekly] = ShowGrokIcon;
            TraySquareVisibility[TraySquareKeys.AgyWeekly] = ShowAgyIcon;
        }

        if (seedLegacyVisibility && !ShowCodexIcon && !ShowGrokIcon && !ShowAgyIcon)
        {
            ShowCodexIcon = true;
            TraySquareVisibility[TraySquareKeys.CodexFiveHour] = true;
        }

        if (!HasKnownVisibleTraySquare())
        {
            TraySquareVisibility[TraySquareKeys.CodexFiveHour] = true;
        }
    }

    private bool HasKnownVisibleTraySquare() =>
        TraySquareVisibility.Values.Any(static visible => visible) ||
        IsTraySquareVisible(TraySquareKeys.CodexFiveHour, ProviderKind.Codex) ||
        IsTraySquareVisible(TraySquareKeys.CodexSevenDay, ProviderKind.Codex) ||
        IsTraySquareVisible(TraySquareKeys.GrokWeekly, ProviderKind.Grok) ||
        IsTraySquareVisible(TraySquareKeys.AgyWeekly, ProviderKind.Agy);
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
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();

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
