using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;

namespace AutoSettings.Core.Tests;

public class MatchingTests
{
    private static readonly ProcessInfo Chrome = new(1, "chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe");

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("chrome.exe", true)]
    [InlineData("CHROME.EXE", true)]
    [InlineData("chr*", true)]
    [InlineData("*", true)]
    [InlineData(@"C:\Program Files\Google\*\chrome.exe", true)]
    [InlineData("C:/Program Files/Google/Chrome/Application/chrome.exe", true)]
    [InlineData("firefox", false)]
    [InlineData("chrome.ex", false)]
    [InlineData(@"D:\chrome.exe", false)]
    public void App_patterns(string pattern, bool expected) =>
        Assert.Equal(expected, AppPattern.Matches(pattern, Chrome));

    [Theory]
    [InlineData("samet", true)]
    [InlineData("SAMET", true)]
    [InlineData(@"PC\samet", true)]
    [InlineData(@".\samet", true)]
    [InlineData("S-1-5-21-1-2-3-1001", true)]
    [InlineData("sam*", true)]
    [InlineData("guest", false)]
    [InlineData(@"OTHER\samet", false)]
    public void User_patterns(string pattern, bool expected) =>
        Assert.Equal(expected, UserPattern.Matches(pattern, TestSupport.Samet));

    [Theory]
    [InlineData("22:00", "06:00", 23, true)]
    [InlineData("22:00", "06:00", 5, true)]
    [InlineData("22:00", "06:00", 12, false)]
    [InlineData("09:00", "17:00", 12, true)]
    [InlineData("09:00", "17:00", 17, false)]
    public void Time_windows(string after, string before, int hour, bool expected)
    {
        var condition = new Model.ComponentConfig("time", new Dictionary<string, object?> { ["after"] = after, ["before"] = before });
        var now = new DateTimeOffset(2026, 9, 28, hour, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, TimeCondition.IsMet(condition, now));
    }

    [Fact]
    public void Weekdays_are_checked()
    {
        var condition = new Model.ComponentConfig("time", new Dictionary<string, object?> { ["weekdays"] = new List<string> { "mon" } });
        Assert.True(TimeCondition.IsMet(condition, new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero)));  // Monday
        Assert.False(TimeCondition.IsMet(condition, new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero))); // Tuesday
    }

    [Fact]
    public void Placeholders_are_replaced_and_unknown_ones_kept()
    {
        var values = new Dictionary<string, string?> { ["user"] = "samet", ["app"] = null };
        Assert.Equal("Hi samet, app=, {{ nope }}", Placeholders.Expand("Hi {{user}}, app={{ app }}, {{ nope }}", values));
    }
}
