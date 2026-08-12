using System.Buffers.Binary;
using NAudio.Wave;
using RhythmIsland.Services;

namespace RhythmIsland.Tests;

public sealed class AudioSampleConverterTests
{
    [Fact]
    public void ConvertsFloat32AndAveragesChannels()
    {
        var values = new[] { 1f, -1f, 0.5f, 0.25f };
        var bytes = values.SelectMany(BitConverter.GetBytes).ToArray();
        var result = AudioSampleConverter.ConvertToMono(bytes, bytes.Length, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        Assert.Equal([0f, 0.375f], result, new FloatComparer(0.0001f));
    }

    [Theory]
    [InlineData(16, 0.4999f)]
    [InlineData(24, 0.5f)]
    [InlineData(32, 0.5f)]
    public void ConvertsPcmFormats(int bits, float expected)
    {
        var bytes = bits switch
        {
            16 => new byte[] { 0xFF, 0x3F },
            24 => new byte[] { 0x00, 0x00, 0x40 },
            32 => new byte[] { 0x00, 0x00, 0x00, 0x40 },
            _ => throw new InvalidOperationException()
        };
        var result = AudioSampleConverter.ConvertToMono(bytes, bytes.Length, new WaveFormat(48000, bits, 1));
        Assert.InRange(result.Single(), expected - 0.001f, expected + 0.001f);
    }

    [Fact]
    public void IgnoresTruncatedTrailingFrame()
    {
        var bytes = new byte[] { 0, 0, 0xFF };
        var result = AudioSampleConverter.ConvertToMono(bytes, bytes.Length, new WaveFormat(48000, 16, 1));
        Assert.Single(result);
    }

    [Fact]
    public void RejectsUnsupportedFormat()
    {
        var format = WaveFormat.CreateALawFormat(8000, 1);
        Assert.Throws<NotSupportedException>(() => AudioSampleConverter.ConvertToMono(new byte[8], 8, format));
    }

    private sealed class FloatComparer(float tolerance) : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => Math.Abs(x - y) <= tolerance;
        public int GetHashCode(float obj) => 0;
    }
}
