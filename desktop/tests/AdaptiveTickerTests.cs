using System;
using NimbleWrite.Desktop.Core;
using Xunit;

namespace NimbleWrite.Desktop.Tests;

/// <summary>How often the engine looks at the focused field: fast while things change, slower as they stay still.</summary>
public sealed class AdaptiveTickerTests
{
    static readonly DateTime T0 = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 120)]
    [InlineData(2_900, 120)]
    [InlineData(3_000, 250)]
    [InlineData(19_900, 250)]
    [InlineData(20_000, 500)]
    [InlineData(600_000, 500)]
    public void SlowsDownTheLongerNothingChanges(int msSinceActivity, int wantMs)
    {
        var t = new AdaptiveTicker(T0);
        Assert.Equal(TimeSpan.FromMilliseconds(wantMs), t.Interval(T0.AddMilliseconds(msSinceActivity)));
    }

    [Fact]
    public void AnyActivitySnapsBackToFast()
    {
        var t = new AdaptiveTicker(T0);
        var later = T0.AddMinutes(5);
        Assert.Equal(AdaptiveTicker.Slow, t.Interval(later));
        t.Activity(later);
        Assert.Equal(AdaptiveTicker.Fast, t.Interval(later.AddMilliseconds(100)));
    }

    [Fact]
    public void ActivityNeverMovesTheClockBackwards()
    {
        var t = new AdaptiveTicker(T0);
        t.Activity(T0.AddSeconds(30));
        t.Activity(T0.AddSeconds(10)); // a late report of something older
        Assert.Equal(AdaptiveTicker.Fast, t.Interval(T0.AddSeconds(31)));
    }

    [Fact]
    public void AWakeBringsTheNextTickForward()
    {
        var t = new AdaptiveTicker(T0);
        var lastTick = T0.AddMinutes(1);
        // Idle: the next tick is a slow interval after the last one.
        Assert.Equal(lastTick + AdaptiveTicker.Slow, t.NextTick(lastTick, lastTick));
        // Something happened 50 ms after that tick: look again 120 ms after the last tick, not 500.
        t.Activity(lastTick.AddMilliseconds(50));
        Assert.Equal(lastTick + AdaptiveTicker.Fast, t.NextTick(lastTick, lastTick.AddMilliseconds(50)));
    }
}
