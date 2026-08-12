using NAudio.Dsp;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;

namespace RhythmIsland.Services;

public sealed class SpectrumAnalyzer : ISpectrumAnalyzer
{
    internal const float SilenceFloor = 0.005f;
    internal const int FftSize = 2048;
    internal const int HopSize = 1024;
    private readonly float[] _buffer = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private float[] _smoothed = [];
    private int _buffered;
    private int _sampleRate = 48000;
    private int _barCount = 48;
    private double _sensitivity = 1.0;
    private double _smoothing = 0.65;

    public SpectrumAnalyzer()
    {
        for (var i = 0; i < FftSize; i++)
            _window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1))));
        _smoothed = new float[_barCount];
    }

    public event EventHandler<SpectrumFrame>? FrameProduced;

    public void Configure(int sampleRate, int barCount, double sensitivity, double smoothing)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (!RhythmIslandSettings.AllowedBarCounts.Contains(barCount)) throw new ArgumentOutOfRangeException(nameof(barCount));
        _sampleRate = sampleRate;
        _sensitivity = double.IsFinite(sensitivity) ? Math.Clamp(sensitivity, 0.5, 3.0) : 1.0;
        _smoothing = double.IsFinite(smoothing) ? Math.Clamp(smoothing, 0, 1) : 0.65;
        if (_barCount != barCount)
        {
            _barCount = barCount;
            _smoothed = new float[barCount];
        }
    }

    public void PushSamples(ReadOnlySpan<float> samples)
    {
        while (!samples.IsEmpty)
        {
            var take = Math.Min(samples.Length, FftSize - _buffered);
            for (var i = 0; i < take; i++)
                _buffer[_buffered + i] = float.IsFinite(samples[i]) ? samples[i] : 0f;
            _buffered += take;
            samples = samples[take..];

            if (_buffered == FftSize)
            {
                AnalyzeCurrentWindow();
                Array.Copy(_buffer, HopSize, _buffer, 0, FftSize - HopSize);
                _buffered = FftSize - HopSize;
            }
        }
    }

    public void AdvanceSilence()
    {
        var release = ReleaseCoefficient();
        for (var i = 0; i < _smoothed.Length; i++)
        {
            _smoothed[i] *= (float)(1 - release);
            if (_smoothed[i] < SilenceFloor) _smoothed[i] = 0;
        }
        Publish();
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        Array.Clear(_smoothed);
        _buffered = 0;
    }

    private void AnalyzeCurrentWindow()
    {
        var fft = new Complex[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            fft[i].X = _buffer[i] * _window[i];
            fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, 11, fft);

        var target = new float[_barCount];
        var maximumFrequency = Math.Min(16000d, _sampleRate / 2d);
        for (var band = 0; band < _barCount; band++)
        {
            var startHz = 50 * Math.Pow(maximumFrequency / 50, band / (double)_barCount);
            var endHz = 50 * Math.Pow(maximumFrequency / 50, (band + 1d) / _barCount);
            var startBin = Math.Clamp((int)Math.Floor(startHz * FftSize / _sampleRate), 1, FftSize / 2 - 1);
            var endBin = Math.Clamp((int)Math.Ceiling(endHz * FftSize / _sampleRate), startBin + 1, FftSize / 2);
            double magnitude = 0;
            for (var bin = startBin; bin < endBin; bin++)
            {
                var value = Math.Sqrt(fft[bin].X * fft[bin].X + fft[bin].Y * fft[bin].Y) * 2;
                magnitude = Math.Max(magnitude, value);
            }
            var db = 20 * Math.Log10(Math.Max(magnitude, 1e-8));
            target[band] = (float)Math.Clamp(((db + 80) / 70) * _sensitivity, 0, 1);
        }

        var attack = 0.85 - 0.50 * _smoothing;
        var release = ReleaseCoefficient();
        for (var i = 0; i < _smoothed.Length; i++)
        {
            var coefficient = target[i] >= _smoothed[i] ? attack : release;
            _smoothed[i] += (target[i] - _smoothed[i]) * (float)coefficient;
            if (!float.IsFinite(_smoothed[i])) _smoothed[i] = 0;
            if (_smoothed[i] < SilenceFloor) _smoothed[i] = 0;
        }
        Publish();
    }

    private double ReleaseCoefficient() => 0.35 - 0.27 * _smoothing;

    private void Publish()
    {
        var peak = _smoothed.Length == 0 ? 0 : _smoothed.Max();
        FrameProduced?.Invoke(this, new SpectrumFrame(_smoothed, DateTimeOffset.UtcNow, peak < 0.005f));
    }
}
