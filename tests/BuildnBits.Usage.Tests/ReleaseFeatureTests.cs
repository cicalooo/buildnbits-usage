using BuildnBits.Usage.Core.JsonRpc;
using BuildnBits.Usage.Core.Refresh;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Tests;

public class ReleaseFeatureTests
{
    [Fact]
    public void Settings_default_to_five_minutes_and_clamp_invalid_values()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-settings-v12-" + Guid.NewGuid());
        var store = new AppSettingsStore(dir);
        File.WriteAllText(store.PathOnDisk, """{"refreshIntervalMinutes": 7}""");

        var loaded = store.Load();

        Assert.Equal(5, loaded.RefreshIntervalMinutes);
        Assert.Equal([3, 5, 10], AppSettings.AllowedRefreshIntervalMinutes);
        loaded.RefreshIntervalMinutes = 10;
        store.Save(loaded);
        Assert.Contains("10", File.ReadAllText(store.PathOnDisk), StringComparison.Ordinal);
    }

    [Fact]
    public void App_log_redacts_sensitive_values_and_rotates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-log-" + Guid.NewGuid());
        using var log = new AppLog(dir, maxBytes: 256);

        log.Info("request Authorization: Bearer super-secret-value");
        for (var i = 0; i < 20; i++)
        {
            log.Info($"refresh completed {i} " + new string('x', 60));
        }

        var text = File.ReadAllText(log.PathOnDisk);
        Assert.DoesNotContain("super-secret-value", text, StringComparison.Ordinal);
        Assert.Contains(" INFO ", text, StringComparison.Ordinal);
        Assert.True(File.Exists(log.PathOnDisk + ".1"));
        Assert.True(new FileInfo(log.PathOnDisk).Length <= 256);
    }

    [Fact]
    public void Process_locator_prefers_a_native_executable_over_a_shim()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-locator-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var basePath = Path.Combine(dir, "usage-tool");
        File.WriteAllText(basePath + ".cmd", "@echo off");
        File.WriteAllBytes(basePath + ".exe", [0]);

        var found = ProcessLocator.FindOnPath(basePath);

        Assert.Equal(basePath + ".exe", found);
        var launch = ProcessLocator.PrepareLaunch(basePath + ".cmd", ["--check"]);
        Assert.Equal("cmd.exe", launch.FileName);
        Assert.Contains("/d", launch.Arguments);
    }

    [Fact]
    public void Process_locator_can_run_a_cmd_shim_with_arguments()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "bnb shim " + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var script = Path.Combine(dir, "usage-tool.cmd");
            File.WriteAllText(script, "@echo off\r\necho %1 %2\r\n");
            var launch = ProcessLocator.PrepareLaunch(script, ["--check", "stdio://"]);
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = launch.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (ProcessLocator.UsesCommandShell(launch.FileName))
            {
                start.Arguments = ProcessLocator.BuildRawArguments(launch.Arguments);
            }
            else
            {
                foreach (var argument in launch.Arguments)
                {
                    start.ArgumentList.Add(argument);
                }
            }

            using var process = new System.Diagnostics.Process { StartInfo = start };
            Assert.True(process.Start());
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, $"exit={process.ExitCode}; output={output}; error={error}; args={string.Join("|", launch.Arguments)}");
            Assert.Contains("--check", output, StringComparison.Ordinal);
            Assert.Contains("stdio://", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Shared_process_start_info_is_headless_and_redirected()
    {
        var start = ProcessLocator.CreateStartInfo(
            "usage-tool.exe",
            ["--check", "stdio://"],
            redirectStandardInput: true);

        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Equal(System.Diagnostics.ProcessWindowStyle.Hidden, start.WindowStyle);
        Assert.True(start.RedirectStandardInput);
        Assert.True(start.RedirectStandardOutput);
        Assert.True(start.RedirectStandardError);
        Assert.Equal("usage-tool.exe", start.FileName);
        Assert.Equal(["--check", "stdio://"], start.ArgumentList);
    }

    [Fact]
    public void Refresh_service_interval_can_change_without_recreating_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-refresh-v12-" + Guid.NewGuid());
        using var log = new AppLog(dir);
        using var service = new UsageRefreshService(
            cache: new UsageCache(dir),
            interval: TimeSpan.FromMinutes(10),
            appLog: log);

        service.UpdateInterval(TimeSpan.FromMinutes(3));

        Assert.Equal(TimeSpan.FromMinutes(3), service.Interval);
        Assert.Contains("interval changed", log.ReadRecent(), StringComparison.OrdinalIgnoreCase);
    }
}
