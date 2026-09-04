using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Providers.Agy;

namespace BuildnBits.Usage.Tests;

public class AgyParsingTests
{
    private const string UsageJson = """
        {
          "conversation_id": "",
          "status": "SUCCESS",
          "command": {
            "name": "usage",
            "data": {
              "groups": [
                {
                  "name": "Gemini Models",
                  "buckets": [
                    {
                      "id": "gemini-weekly",
                      "name": "Weekly Limit Remaining",
                      "window": "weekly",
                      "remaining_fraction": 0.8856351971626282,
                      "reset_time": "2026-09-11T21:20:03Z"
                    }
                  ]
                },
                {
                  "name": "Claude and GPT models",
                  "buckets": [
                    {
                      "id": "3p-weekly",
                      "name": "Weekly Limit Remaining",
                      "window": "weekly",
                      "remaining_fraction": 1,
                      "reset_time": "2026-09-11T21:23:17Z"
                    }
                  ]
                }
              ]
            }
          }
        }
        """;

    [Fact]
    public void Parses_each_model_quota_pool()
    {
        var snapshot = AgyUsageParser.Parse(UsageJson, DateTimeOffset.Parse("2026-09-05T00:00:00Z"));

        Assert.Equal(ProviderKind.Agy, snapshot.Provider);
        Assert.Equal(UsageStatus.Ok, snapshot.Status);
        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal(89, snapshot.Windows[0].RemainingPercent);
        Assert.Equal(11, snapshot.Windows[0].UsedPercent);
        Assert.Equal(AgyUsageParser.WeeklyWindowMinutes, snapshot.Windows[0].DurationMinutes);
        Assert.Equal("Gemini Models · Weekly Limit Remaining", snapshot.Windows[0].Label);
        Assert.Equal(100, snapshot.Windows[1].RemainingPercent);
        Assert.Equal(11, snapshot.Windows[1].ResetsAtUtc!.Value.Day);
        Assert.Equal(89, snapshot.LowestRemainingPercent);
        Assert.Equal(89, snapshot.Weekly!.RemainingPercent);
    }

    [Fact]
    public void Supports_reset_countdown_when_only_seconds_are_returned()
    {
        const string json = """
            {
              "command": {
                "data": {
                  "groups": [
                    {
                      "name": "Gemini Models",
                      "buckets": [
                        { "name": "Weekly Limit Remaining", "remaining_fraction": 0.5, "reset_in_seconds": 3600 }
                      ]
                    }
                  ]
                }
              }
            }
            """;
        var now = DateTimeOffset.Parse("2026-09-05T00:00:00Z");

        var window = AgyUsageParser.Parse(json, now).Windows.Single();

        Assert.Equal(now.AddHours(1), window.ResetsAtUtc);
        Assert.Equal(50, window.RemainingPercent);
    }

    [Fact]
    public void Rejects_a_command_error_payload()
    {
        const string json = """
            { "status": "ERROR", "error": { "message": "authentication required" } }
            """;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AgyUsageParser.Parse(json, DateTimeOffset.UtcNow));

        Assert.Contains("authentication required", exception.Message);
    }

    [Fact]
    public async Task Client_uses_the_read_only_usage_command()
    {
        IReadOnlyList<string>? seenArguments = null;
        var client = new AgyUsageClient(
            Environment.ProcessPath!,
            (_, arguments, _) =>
            {
                seenArguments = arguments.ToArray();
                return Task.FromResult(new AgyCommandResult(0, UsageJson, ""));
            });

        var snapshot = await client.FetchAsync(CancellationToken.None);

        Assert.Equal(UsageStatus.Ok, snapshot.Status);
        Assert.Equal(["-p", "/usage", "--output-format", "json"], seenArguments);
    }

    [Fact]
    public async Task Missing_cli_is_reported_without_reading_credentials()
    {
        var client = new AgyUsageClient("agy-definitely-missing.exe");

        var snapshot = await client.FetchAsync(CancellationToken.None);

        Assert.Equal(ProviderKind.Agy, snapshot.Provider);
        Assert.Equal(UsageStatus.MissingCli, snapshot.Status);
    }
}
