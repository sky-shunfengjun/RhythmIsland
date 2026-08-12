using System.Buffers.Binary;
using NAudio.Wave;

namespace RhythmIsland.Services;

internal static class AudioSampleConverter
{
    public static float[] ConvertToMono(byte[] buffer, int byteCount, WaveFormat waveFormat)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(waveFormat);
        if (byteCount < 0 || byteCount > buffer.Length) throw new ArgumentOutOfRangeException(nameof(byteCount));

        var format = waveFormat is WaveFormatExtensible extensible ? extensible.ToStandardWaveFormat() : waveFormat;
        var bytesPerSample = format.BitsPerSample / 8;
        if (format.Channels <= 0 || bytesPerSample <= 0) throw new NotSupportedException("无效的音频格式。");
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample != 32)
            throw new NotSupportedException("仅支持 32 位浮点音频。");
        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample is not (16 or 24 or 32))
            throw new NotSupportedException("仅支持 16/24/32 位 PCM 音频。");
        if (format.Encoding is not (WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Pcm))
            throw new NotSupportedException($"不支持音频格式 {format.Encoding}。");

        var frameSize = bytesPerSample * format.Channels;
        var frameCount = byteCount / frameSize;
        var samples = new float[frameCount];
        var span = buffer.AsSpan(0, frameCount * frameSize);

        for (var frame = 0; frame < frameCount; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var offset = frame * frameSize + channel * bytesPerSample;
                sum += ReadSample(span.Slice(offset, bytesPerSample), format.Encoding, format.BitsPerSample);
            }

            var averaged = (float)(sum / format.Channels);
            samples[frame] = float.IsFinite(averaged) ? Math.Clamp(averaged, -1f, 1f) : 0f;
        }

        return samples;
    }

    private static float ReadSample(ReadOnlySpan<byte> source, WaveFormatEncoding encoding, int bits)
    {
        if (encoding == WaveFormatEncoding.IeeeFloat)
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source));

        return bits switch
        {
            16 => BinaryPrimitives.ReadInt16LittleEndian(source) / 32768f,
            24 => ReadInt24(source) / 8388608f,
            32 => BinaryPrimitives.ReadInt32LittleEndian(source) / 2147483648f,
            _ => throw new NotSupportedException()
        };
    }

    private static int ReadInt24(ReadOnlySpan<byte> source)
    {
        var value = source[0] | (source[1] << 8) | (source[2] << 16);
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }
}
