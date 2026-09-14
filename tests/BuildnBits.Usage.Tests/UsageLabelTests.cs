using BuildnBits.Usage.Core.Parsing;

namespace BuildnBits.Usage.Tests;

public class UsageLabelTests
{
    [Theory]
    [InlineData("5-hour", "5h")]
    [InlineData("7-day", "7d")]
    [InlineData("Weekly", "Build")]
    [InlineData("Build", "Build")]
    [InlineData("Bot", "Bot")]
    [InlineData("Gemini Models · Weekly Limit Remaining", "Gemini 7d")]
    [InlineData("Gemini Models · 7-day Limit Remaining", "Gemini 7d")]
    [InlineData("Gemini Models · Five Hour Limit Remaining", "Gemini 5h")]
    [InlineData("Claude and GPT models · Weekly Limit Remaining", "Claude 7d")]
    [InlineData("Claude and GPT models · 7-day Limit Remaining", "Claude 7d")]
    [InlineData("Claude and GPT models · Five Hour Limit Remaining", "Claude 5h")]
    public void Condenses_labels(string raw, string expected) =>
        Assert.Equal(expected, UsageLabel.Display(raw));
}
