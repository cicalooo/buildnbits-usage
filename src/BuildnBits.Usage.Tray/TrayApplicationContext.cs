using System.Net.NetworkInformation;
using BuildnBits.Usage.Core.Refresh;
using BuildnBits.Usage.Core.Providers.Agy;
using BuildnBits.Usage.Core.Providers.Codex;
using BuildnBits.Usage.Core.Providers.Grok;
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
    private readonly UsagePopupForm _widget = new();
    private readonly SynchronizationContext? _ui;
    private AppSettings _settings;
    private bool _launchAtLogin;
    private int _exiting;

    public TrayApplicationContext()
    {
        _settings = _settingsStore.Load();
        _log.Info($"Application startup; refresh interval={_settings.RefreshIntervalMinutes} minutes.");
        _refresh = new UsageRefreshService(
            new CodexUsageClient(appLog: _log),
            new GrokUsageClient(appLog: _log),
            new AgyUsageClient(appLog: _log),
            cache: _cache,
            interval: TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes),
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
            ApplyIconsOnUi();
        };
        _icons.FloatingWidgetToggled += (_, enabled) =>
        {
            _settings.FloatingWidgetEnabled = enabled;
            if (enabled)
            {
                if (_settings.FloatingWidgetX is null || _settings.FloatingWidgetY is null)
                {
                    var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
                    _settings.FloatingWidgetX = area.Right - 430 - 24;
                    _settings.FloatingWidgetY = area.Top + 24;
                }
            }

            _settingsStore.Save(_settings);
            ApplyFloatingWidgetState(forceShow: enabled);
            ApplyIconsOnUi();
        };

        WirePanel(_popup, isWidget: false);
        WirePanel(_widget, isWidget: true);

        var ui = SynchronizationContext.Current;
        _ui = ui;
        _refresh.StateChanged += (_, state) =>
        {
            void Apply()
            {
                if (Volatile.Read(ref _exiting) != 0)
                {
                    return;
                }

                _icons.Apply(state, _launchAtLogin, _settings);
                if (_popup.Visible)
                {
                    _popup.Bind(state, _launchAtLogin, _settings, pinned: false);
                }

                if (_widget.Visible || _settings.FloatingWidgetEnabled)
                {
                    _widget.Bind(state, _launchAtLogin, _settings, pinned: true);
                }
            }

            if (ui is not null)
            {
                ui.Post(_ => Apply(), null);
            }
            else if (_popup.IsHandleCreated && !_popup.IsDisposed)
            {
                try
                {
                    _popup.BeginInvoke(Apply);
                }
                catch (InvalidOperationException)
                {
                    // The popup is closing while a state update is queued.
                }
            }
            else
            {
                Apply();
            }
        };
        _refresh.ProgressChanged += (_, progress) =>
        {
            void Apply()
            {
                if (Volatile.Read(ref _exiting) != 0)
                {
                    return;
                }

                if (!_popup.IsDisposed)
                {
                    _popup.SetRefreshProgress(progress);
                }

                if (!_widget.IsDisposed)
                {
                    _widget.SetRefreshProgress(progress);
                }
            }

            if (ui is not null)
            {
                ui.Post(_ => Apply(), null);
            }
            else if (_popup.IsHandleCreated && !_popup.IsDisposed)
            {
                try
                {
                    _popup.BeginInvoke(Apply);
                }
                catch (InvalidOperationException)
                {
                    // The popup is closing while a progress update is queued.
                }
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
        ApplyFloatingWidgetState(forceShow: _settings.FloatingWidgetEnabled);
    }

    private void WirePanel(UsagePopupForm panel, bool isWidget)
    {
        panel.RefreshClicked += (_, _) => RequestRefresh();
        panel.SettingsClicked += (_, _) => OpenSettings();
        panel.DiagnosticsClicked += (_, _) => new DiagnosticsForm(_refresh.Current, _log).Show();
        panel.ExitClicked += (_, _) => ExitThread();
        panel.LaunchAtLoginChanged += (_, enabled) =>
        {
            _launchAtLogin = enabled;
            LaunchAtLogin.SetEnabled(enabled);
            ApplyIconsOnUi();
        };
        panel.PinWidgetChanged += (_, enabled) =>
        {
            _settings.FloatingWidgetEnabled = enabled;
            if (enabled)
            {
                _settings.FloatingWidgetX = panel.Location.X;
                _settings.FloatingWidgetY = panel.Location.Y;
            }

            _settingsStore.Save(_settings);
            if (!isWidget && enabled)
            {
                panel.SetPinned(false, raiseEvent: false);
            }

            ApplyFloatingWidgetState(forceShow: enabled);
            ApplyIconsOnUi();
        };
        panel.PinnedLocationChanged += (_, _) =>
        {
            if (!_settings.FloatingWidgetEnabled || !isWidget)
            {
                return;
            }

            _settings.FloatingWidgetX = panel.Location.X;
            _settings.FloatingWidgetY = panel.Location.Y;
            _settingsStore.Save(_settings);
        };
    }

    private void ShowPopup()
    {
        _popup.Bind(
            _refresh.Current,
            _launchAtLogin,
            _settings,
            pinned: false);
        _popup.ShowNearCursor();
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(
            _settings,
            _settingsStore,
            _cache.PathOnDisk,
            _refresh.Current);
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var wasFloating = _settings.FloatingWidgetEnabled;
            _settings = dialog.Result;
            _launchAtLogin = LaunchAtLogin.IsEnabled();
            _refresh.UpdateInterval(TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes));
            _icons.Apply(_refresh.Current, _launchAtLogin, _settings);
            ApplyFloatingWidgetState(forceShow: _settings.FloatingWidgetEnabled && !wasFloating);
            if (_popup.Visible)
            {
                _popup.Bind(
                    _refresh.Current,
                    _launchAtLogin,
                    _settings,
                    pinned: false);
            }
        }
    }

    private void ApplyFloatingWidgetState(bool forceShow)
    {
        if (_settings.FloatingWidgetEnabled)
        {
            _widget.Bind(_refresh.Current, _launchAtLogin, _settings, pinned: true);
            if (_settings.FloatingWidgetX is not null && _settings.FloatingWidgetY is not null)
            {
                _widget.ApplyFloatingLocation(_settings.FloatingWidgetX, _settings.FloatingWidgetY);
            }
            else if (forceShow || !_widget.Visible)
            {
                var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
                _widget.ApplyFloatingLocation(area.Right - _widget.Width - 24, area.Top + 24);
                _settings.FloatingWidgetX = _widget.Location.X;
                _settings.FloatingWidgetY = _widget.Location.Y;
                _settingsStore.Save(_settings);
            }

            _widget.SetPinned(true, raiseEvent: false);
            return;
        }

        if (_widget.IsPinned || _widget.Visible)
        {
            _widget.SetPinned(false, raiseEvent: false);
            _widget.Hide();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            RequestRefresh();
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            RequestRefresh();
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        PostToUi(ApplyIconsOnUi);
    }

    private void PostToUi(Action action)
    {
        if (Volatile.Read(ref _exiting) != 0)
        {
            return;
        }

        if (_ui is not null)
        {
            _ui.Post(_ =>
            {
                if (Volatile.Read(ref _exiting) == 0)
                {
                    action();
                }
            }, null);
            return;
        }

        if (_popup.IsHandleCreated && !_popup.IsDisposed)
        {
            try
            {
                _popup.BeginInvoke(() =>
                {
                    if (Volatile.Read(ref _exiting) == 0)
                    {
                        action();
                    }
                });
            }
            catch (InvalidOperationException)
            {
                // The popup is closing while a preference update is queued.
            }
        }
    }

    private void ApplyIconsOnUi()
    {
        if (Volatile.Read(ref _exiting) != 0 || _popup.IsDisposed)
        {
            return;
        }

        _icons.Apply(_refresh.Current, _launchAtLogin, _settings);
    }

    protected override void ExitThreadCore()
    {
        Interlocked.Exchange(ref _exiting, 1);
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _refresh.Dispose();
        _icons.Dispose();
        _popup.Dispose();
        _widget.Dispose();
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
            _popup.Bind(
                _refresh.Current,
                _launchAtLogin,
                _settings,
                pinned: false);
        }

        if (_widget.Visible || _settings.FloatingWidgetEnabled)
        {
            _widget.Bind(
                _refresh.Current,
                _launchAtLogin,
                _settings,
                pinned: true);
        }
    }
}
