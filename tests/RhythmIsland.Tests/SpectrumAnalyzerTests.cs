using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Tests;

public sealed class SpectrumAnalyzerTests
{
    [Theory]
    [InlineData(44100, 735)]
    [InlineData(48000, 800)]
    [InlineData(96000, 1600)]
    [InlineData(192000, 3200)]
    public void HopSizeTargetsSixtyAnalysisFramesPerSecond(int sampleRate, int expectedHopSize)
    {
        Assert.Equal(expectedHopSize, SpectrumAnalyzer.CalculateHopSize(sampleRate));
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    [InlineData(192000)]
    public void SteadyStateProducesAboutSixtyFramesPerSecond(int sampleRate)
    {
        var analyzer = new SpectrumAnalyzer();
        analyzer.Configure(sampleRate, 48, 1, 0.65);
        var frames = 0;
        analyzer.FrameProduced += (_, _) => frames++;

        analyzer.PushSamples(new float[SpectrumAnalyzer.FftSize + sampleRate]);

        Assert.InRange(frames - 1, 59, 61);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(96)]
    public void ProducesRequestedFiniteBarCount(int barCount)
    {
        var frame = Analyze(1000, barCount: barCount);
        Assert.Equal(barCount, frame.Bands.Count);
        Assert.All(frame.Bands, value => Assert.True(float.IsFinite(value) && value is >= 0 and <= 1));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(8000)]
    public void SineWavePeaksNearExpectedLogBand(double frequency)
    {
        const int bars = 48;
        var frame = Analyze(frequency, bars);
        var actual = frame.Bands.IndexOf(frame.Bands.Max());
        var expected = (int)Math.Floor(Math.Log(frequency / 50) / Math.Log(16000d / 50) * bars);
        Assert.InRange(actual, Math.Max(0, expected - 2), Math.Min(bars - 1, expected + 2));
        Assert.True(frame.Peak > 0.05f);
    }

    [Fact]
    public void SilenceStaysFiniteAndSilent()
    {
        var analyzer = CreateAnalyzer();
        SpectrumFrame? frame = null;
        analyzer.FrameProduced += (_, value) => frame = value;
        analyzer.PushSamples(new float[4096]);
        Assert.NotNull(frame);
        Assert.True(frame!.IsSilent);
        Assert.All(frame.Bands, value => Assert.InRange(value, 0, 0.001f));
    }

    [Fact]
    public void HigherSensitivityDoesNotReducePeak()
    {
        var low = Analyze(1000, sensitivity: 0.5).Peak;
        var high = Analyze(1000, sensitivity: 2.0).Peak;
        Assert.True(high >= low);
    }

    [Fact]
    public void AttackIsFasterThanReleaseAndSilenceEventuallyReachesZero()
    {
        var analyzer = CreateAnalyzer(smoothing: 0.65);
        SpectrumFrame? frame = null;
        analyzer.FrameProduced += (_, value) => frame = value;
        analyzer.PushSamples(CreateSine(1000, 4096));
        var raised = frame!.Peak;
        analyzer.AdvanceSilence();
        var firstDecay = frame!.Peak;
        Assert.True(raised > 0.05f && firstDecay > 0 && firstDecay < raised);
        for (var i = 0; i < 240; i++) analyzer.AdvanceSilence();
        Assert.True(frame!.Peak < 0.001f);
        Assert.True(frame.IsSilent);
        Assert.All(frame.Bands, value => Assert.Equal(0, value));
    }

    [Fact]
    public void DefaultSmoothingAppliesAtLeastEightyPercentOfInitialAttack()
    {
        var analyzer = CreateAnalyzer(smoothing: 0.65);
        SpectrumFrame? frame = null;
        analyzer.FrameProduced += (_, value) => frame = value;
        analyzer.PushSamples(CreateSine(1000, SpectrumAnalyzer.FftSize));

        var first = frame!.Peak;
        Assert.True(first > 0.70f);
    }

    [Fact]
    public void NonFiniteInputCannotPoisonFrame()
    {
        var samples = CreateSine(1000, 4096);
        samples[100] = float.NaN;
        samples[200] = float.PositiveInfinity;
        var analyzer = CreateAnalyzer();
        SpectrumFrame? frame = null;
        analyzer.FrameProduced += (_, value) => frame = value;
        analyzer.PushSamples(samples);
        Assert.All(frame!.Bands, value => Assert.True(float.IsFinite(value)));
    }

    private static SpectrumAnalyzer CreateAnalyzer(int bars = 48, double sensitivity = 1, double smoothing = 0.65)
    {
        var analyzer = new SpectrumAnalyzer();
        analyzer.Configure(48000, bars, sensitivity, smoothing);
        return analyzer;
    }

    private static SpectrumFrame Analyze(double frequency, int barCount = 48, double sensitivity = 1)
    {
        var analyzer = CreateAnalyzer(barCount, sensitivity);
        SpectrumFrame? frame = null;
        analyzer.FrameProduced += (_, value) => frame = value;
        analyzer.PushSamples(CreateSine(frequency, 8192));
        return frame!;
    }

    private static float[] CreateSine(double frequency, int count)
    {
        var samples = new float[count];
        for (var i = 0; i < count; i++) samples[i] = (float)(0.8 * Math.Sin(2 * Math.PI * frequency * i / 48000));
        return samples;
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T target)
    {
        for (var i = 0; i < values.Count; i++) if (EqualityComparer<T>.Default.Equals(values[i], target)) return i;
        return -1;
    }
}
