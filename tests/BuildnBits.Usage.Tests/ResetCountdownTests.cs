using BuildnBits.Usage.Core.Parsing;

namespace BuildnBits.Usage.Tests;

public class ResetCountdownTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 11, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Caption_null_reset()
    {
        Assert.Equal("—", ResetCountdown.Caption(null, Now));
    }

    [Fact]
    public void Caption_now_or_negative()
    {
        var reset = Now;
        Assert.Equal($"now · {reset.ToLocalTime():ddd HH:mm}", ResetCountdown.Caption(reset, Now));
        Assert.Equal($"now · {reset.AddMinutes(-1).ToLocalTime():ddd HH:mm}", ResetCountdown.Caption(reset.AddMinutes(-1), Now));
    }

    [Fact]
    public void Caption_minutes_only()
    {
        var reset = Now.AddMinutes(12);
        Assert.Equal($"12m · {reset.ToLocalTime():ddd HH:mm}", ResetCountdown.Caption(reset, Now));
    }

    [Fact]
    public void Caption_hours_and_minutes()
    {
        var reset = Now.AddHours(4).AddMinutes(12);
        Assert.Equal($"4h 12m · {reset.ToLocalTime():ddd HH:mm}", ResetCountdown.Caption(reset, Now));
    }

    [Fact]
    public void Caption_hours_omit_zero_minutes()
    {
        var reset = Now.AddHours(4);
        Assert.Equal($"4h · {reset.ToLocalTime():ddd HH:mm}", ResetCountdown.Caption(reset, Now));
    }

    [Fact]
    public void Caption_days_and_hours()
    {
        var reset = Now.AddDays(2).AddHours(4).AddMinutes(24);
        Assert.Equal($"2d 4h · {reset.ToLocalTime():ddd HH:mm}", ResetCountdown.Caption(reset, Now));
    }

    [Fact]
    public void Caption_days_omit_zero_hours()
    {
        var reset = Now.AddDays(2);
        Assert.Equal($"2d · {reset.ToLocalTime():ddd HH:mm}", ResetCountdown.Caption(reset, Now));
    }

    [Fact]
    public void FormatCompact_has_no_seconds()
    {
        Assert.Equal("12m", ResetCountdown.FormatCompact(TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(45))));
        Assert.DoesNotContain("s", ResetCountdown.FormatCompact(TimeSpan.FromSeconds(45)));
    }
}
