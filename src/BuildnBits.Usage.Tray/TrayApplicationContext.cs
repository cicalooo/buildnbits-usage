using System.Net.NetworkInformation;
using BuildnBits.Usage.Core.Refresh;
using BuildnBits.Usage.Core.Providers.Agy;
using BuildnBits.Usage.Core.Storage;
using BuildnBits.Usage.Tray.Icons;
using BuildnBits.Usage.Tray.Startup;
using BuildnBits.Usage.Tray.Ui;
using Microsoft.Win32;

namespace BuildnBits.Usage.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly UsageCache _cache = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly AppLog _log = new();
    private readonly UsageRefreshService _refresh;
    private readonly NotifyIconHost _icons = new();
    private readonly UsagePopupForm _popup = new();
    private AppSettings _settings;
    private bool _launchAtLogin;

    public TrayApplicationContext()
    {
        _settings = _settingsStore.Load();
        _log.Info($"Application startup; refresh interval={_settings.RefreshIntervalMinutes} minutes.");
        _refresh = new UsageRefreshService(
            cache: _cache,
            interval: TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes),
            agy: new AgyUsageClient(),
            appLog: _log);
        _launchAtLogin = LaunchAtLogin.IsEnabled();

        _icons.PopupRequested += (_, _) => ShowPopup();
        _icons.RefreshRequested += (_, _) => RequestRefresh();
        _icons.RefreshIntervalRequested += (_, minutes) => SetRefreshInterval(minutes);
        _icons.SettingsRequested += (_, _) => OpenSettings();
        _icons.DiagnosticsRequested += (_, _) => new DiagnosticsForm(_refresh.Current, _log).Show();
        _icons.ExitRequested += (_, _) => ExitThread();
        _icons.LaunchAtLoginToggled += (_, enabled) =>
        {
            _launchAtLogin = enabled;
            LaunchAtLogin.SetEnabled(enabled);
        };

        _popup.RefreshClicked += (_, _) => RequestRefresh();
        _popup.SettingsClicked += (_, _) => OpenSettings();
        _popup.DiagnosticsClicked += (_, _) => new DiagnosticsForm(_refresh.Current, _log).Show();
        _popup.ExitClicked += (_, _) => ExitThread();
        _popup.LaunchAtLoginChanged += (_, enabled) =>
        {
            _launchAtLogin = enabled;
            LaunchAtLogin.SetEnabled(enabled);
        };

        var ui = SynchronizationContext.Current;
        _refresh.StateChanged += (_, state) =>
        {
            void Apply()
            {
                _icons.Apply(state, _launchAtLogin, _settings);
                if (_popup.Visible)
                {
                    _popup.Bind(state, _launchAtLogin);
                }
            }

            if (ui is not null)
            {
                ui.Post(_ => Apply(), null);
            }
            else if (_popup.IsHandleCreated)
            {
                _popup.BeginInvoke(Apply);
            }
            else
            {
                Apply();
            }
        };

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        _icons.Apply(_refresh.Current, _launchAtLogin, _settings);
        _refresh.Start();
    }

    private void ShowPopup()
    {
        _popup.Bind(_refresh.Current, _launchAtLogin);
        _popup.ShowNearCursor();
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_settings, _settingsStore, _cache.PathOnDisk);
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _settings = dialog.Result;
            _launchAtLogin = LaunchAtLogin.IsEnabled();
            _refresh.UpdateInterval(TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes));
            _icons.Apply(_refresh.Current, _launchAtLogin, _settings);
            if (_popup.Visible)
            {
                _popup.Bind(_refresh.Current, _launchAtLogin);
            }
        }
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            await _refresh.RefreshNowAsync();
        }
    }

    private async void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            await _refresh.RefreshNowAsync();
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        _icons.Apply(_refresh.Current, _launchAtLogin, _settings);
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _refresh.Dispose();
        _icons.Dispose();
        _popup.Dispose();
        _log.Dispose();
        base.ExitThreadCore();
    }

    private void RequestRefresh()
    {
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            await _refresh.RefreshNowAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Shutdown can cancel a manual refresh.
        }
        catch (Exception ex)
        {
            _log.Error($"Manual refresh failed: {ex.Message}");
        }
    }

    private void SetRefreshInterval(int minutes)
    {
        var clamped = AppSettings.ClampRefreshIntervalMinutes(minutes);
        _settings.RefreshIntervalMinutes = clamped;
        _settingsStore.Save(_settings);
        _refresh.UpdateInterval(TimeSpan.FromMinutes(clamped));
        _icons.Apply(_refresh.Current, _launchAtLogin, _settings);
        if (_popup.Visible)
        {
            _popup.Bind(_refresh.Current, _launchAtLogin);
        }
    }
}
