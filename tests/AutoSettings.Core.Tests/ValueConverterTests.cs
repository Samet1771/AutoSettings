using AutoSettings.Core.Model;

namespace AutoSettings.Core.Tests;

public class ValueConverterTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h30m", 5400)]
    [InlineData("1h 30m", 5400)]
    [InlineData("2d", 172800)]
    [InlineData("00:05:00", 300)]
    [InlineData("90", 90)]
    [InlineData("1.5", 1.5)]
    public void Parses_durations(string text, double seconds)
    {
        Assert.True(ValueConverter.TryParseDuration(text, out var value));
        Assert.Equal(TimeSpan.FromSeconds(seconds), value);
    }

    [Fact]
    public void Parses_milliseconds()
    {
        Assert.True(ValueConverter.TryParseDuration("250ms", out var value));
        Assert.Equal(TimeSpan.FromMilliseconds(250), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("5x")]
    [InlineData("m5")]
    [InlineData("-5s")]
    [InlineData("5s junk")]
    public void Rejects_invalid_durations(string text) =>
        Assert.False(ValueConverter.TryParseDuration(text, out _));

    [Theory]
    [InlineData(5400, "1h30m")]
    [InlineData(30, "30s")]
    [InlineData(0, "0s")]
    [InlineData(90061, "1d1h1m1s")]
    public void Formats_durations(int seconds, string expected) =>
        Assert.Equal(expected, ValueConverter.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData("true", true)]
    [InlineData("Yes", true)]
    [InlineData("on", true)]
    [InlineData("false", false)]
    [InlineData("OFF", false)]
    [InlineData("no", false)]
    public void Parses_booleans(string text, bool expected)
    {
        Assert.True(ValueConverter.TryToBoolean(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("22:00", 22, 0)]
    [InlineData("7:30", 7, 30)]
    [InlineData("07:30:15", 7, 30)]
    public void Parses_times(string text, int hour, int minute)
    {
        Assert.True(ValueConverter.TryToTime(text, out var time));
        Assert.Equal(hour, time.Hour);
        Assert.Equal(minute, time.Minute);
    }

    [Fact]
    public void Single_value_becomes_a_list()
    {
        Assert.True(ValueConverter.TryToStringList("chrome.exe", out var list));
        Assert.Equal(new[] { "chrome.exe" }, list);
    }
}
