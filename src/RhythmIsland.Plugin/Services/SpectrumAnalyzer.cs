using NAudio.Dsp;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;

namespace RhythmIsland.Services;

public sealed class SpectrumAnalyzer : ISpectrumAnalyzer
{
    internal const float SilenceFloor = 0.005f;
    internal const int FftSize = 2048;
    internal const int TargetFramesPerSecond = 60;
    private readonly float[] _buffer = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private float[] _smoothed = [];
    private float[] _target = [];
    private int _buffered;
    private int _samplesToSkip;
    private int _sampleRate = 48000;
    private int _hopSize = CalculateHopSize(48000);
    private int _barCount = 48;
    private double _sensitivity = 1.0;
    private double _smoothing = 0.65;

    public SpectrumAnalyzer()
    {
        for (var i = 0; i < FftSize; i++)
            _window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1))));
        _smoothed = new float[_barCount];
        _target = new float[_barCount];
    }

    public event EventHandler<SpectrumFrame>? FrameProduced;

    public void Configure(int sampleRate, int barCount, double sensitivity, double smoothing)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (!RhythmIslandSettings.AllowedBarCounts.Contains(barCount)) throw new ArgumentOutOfRangeException(nameof(barCount));
        if (_sampleRate != sampleRate)
        {
            _sampleRate = sampleRate;
            _hopSize = CalculateHopSize(sampleRate);
            Array.Clear(_buffer);
            _buffered = 0;
            _samplesToSkip = 0;
        }
        _sensitivity = double.IsFinite(sensitivity) ? Math.Clamp(sensitivity, 0.5, 3.0) : 1.0;
        _smoothing = double.IsFinite(smoothing) ? Math.Clamp(smoothing, 0, 1) : 0.65;
        if (_barCount != barCount)
        {
            _barCount = barCount;
            _smoothed = new float[barCount];
            _target = new float[barCount];
        }
    }

    public void PushSamples(ReadOnlySpan<float> samples)
    {
        while (!samples.IsEmpty)
        {
            if (_samplesToSkip > 0)
            {
                var skip = Math.Min(samples.Length, _samplesToSkip);
                samples = samples[skip..];
                _samplesToSkip -= skip;
                continue;
            }

            var take = Math.Min(samples.Length, FftSize - _buffered);
            for (var i = 0; i < take; i++)
                _buffer[_buffered + i] = float.IsFinite(samples[i]) ? samples[i] : 0f;
            _buffered += take;
            samples = samples[take..];

            if (_buffered == FftSize)
            {
                AnalyzeCurrentWindow();
                if (_hopSize < FftSize)
                {
                    Array.Copy(_buffer, _hopSize, _buffer, 0, FftSize - _hopSize);
                    _buffered = FftSize - _hopSize;
                }
                else
                {
                    _buffered = 0;
                    _samplesToSkip = _hopSize - FftSize;
                }
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
        _samplesToSkip = 0;
    }

    private void AnalyzeCurrentWindow()
    {
        for (var i = 0; i < FftSize; i++)
        {
            _fft[i].X = _buffer[i] * _window[i];
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, 11, _fft);

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
                var value = Math.Sqrt(_fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y) * 2;
                magnitude = Math.Max(magnitude, value);
            }
            var db = 20 * Math.Log10(Math.Max(magnitude, 1e-8));
            _target[band] = (float)Math.Clamp(((db + 80) / 70) * _sensitivity, 0, 1);
        }

        var attack = 0.90 - 0.10 * _smoothing;
        var release = ReleaseCoefficient();
        for (var i = 0; i < _smoothed.Length; i++)
        {
            var coefficient = _target[i] >= _smoothed[i] ? attack : release;
            _smoothed[i] += (_target[i] - _smoothed[i]) * (float)coefficient;
            if (!float.IsFinite(_smoothed[i])) _smoothed[i] = 0;
            if (_smoothed[i] < SilenceFloor) _smoothed[i] = 0;
        }
        Publish();
    }

    private double ReleaseCoefficient() => 0.35 - 0.27 * _smoothing;

    internal static int CalculateHopSize(int sampleRate)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        return Math.Max(1, (int)Math.Round(sampleRate / (double)TargetFramesPerSecond));
    }

    private void Publish()
    {
        var peak = _smoothed.Length == 0 ? 0 : _smoothed.Max();
        FrameProduced?.Invoke(this, new SpectrumFrame(_smoothed, DateTimeOffset.UtcNow, peak < 0.005f));
    }
}
