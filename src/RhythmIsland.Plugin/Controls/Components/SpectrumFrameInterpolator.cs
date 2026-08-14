using RhythmIsland.Models;

namespace RhythmIsland.Controls.Components;

internal sealed class SpectrumFrameInterpolator
{
    private static readonly TimeSpan DefaultTransitionDuration = TimeSpan.FromMilliseconds(15);
    private const float ImmediateAttackShare = 0.80f;
    private SpectrumFrame? _observedFrame;
    private float[] _displayed = [];
    private float[] _transitionStart = [];
    private float[] _target = [];
    private float[] _resampleBuffer = [];
    private DateTimeOffset _transitionStartedAt;
    private DateTimeOffset? _lastFrameAcceptedAt;
    private TimeSpan _transitionDuration = DefaultTransitionDuration;
    private int _detailCount;
    private double _amplitude;
    private SpectrumFrequencyBalanceMode _balanceMode;
    private bool _horizontalMirrorEnabled;

    internal int BufferCapacity => _displayed.Length;

    internal IReadOnlyList<float> Resolve(
        SpectrumFrame frame,
        int detailCount,
        double amplitude,
        int frameRate,
        DateTimeOffset now,
        SpectrumFrequencyBalanceMode balanceMode = SpectrumFrequencyBalanceMode.Original,
        bool horizontalMirrorEnabled = false)
    {
        var configurationChanged = detailCount != _detailCount
                                   || Math.Abs(amplitude - _amplitude) > 0.000001
                                   || balanceMode != _balanceMode
                                   || horizontalMirrorEnabled != _horizontalMirrorEnabled;
        EnsureCapacity(detailCount, horizontalMirrorEnabled);

        if (frame.IsSilent || frame.Bands.Count == 0)
        {
            Reset();
            return _displayed;
        }

        if (_observedFrame is null || configurationChanged || frameRate is 30 or 60)
        {
            BuildTarget(frame, detailCount, amplitude, balanceMode, horizontalMirrorEnabled);
            Array.Copy(_target, _displayed, detailCount);
            Array.Copy(_target, _transitionStart, detailCount);
            AcceptFrame(frame, detailCount, amplitude, balanceMode, horizontalMirrorEnabled, now);
            return _displayed;
        }

        if (ReferenceEquals(frame, _observedFrame))
        {
            Advance(now);
            return _displayed;
        }

        Advance(now);
        BuildTarget(frame, detailCount, amplitude, balanceMode, horizontalMirrorEnabled);
        for (var index = 0; index < detailCount; index++)
        {
            if (_target[index] > _displayed[index])
                _displayed[index] += (_target[index] - _displayed[index]) * ImmediateAttackShare;
            _transitionStart[index] = _displayed[index];
        }

        var observedInterval = _lastFrameAcceptedAt is { } last
            ? now - last
            : DefaultTransitionDuration;
        _transitionDuration = CalculateTransitionDuration(observedInterval);
        AcceptFrame(frame, detailCount, amplitude, balanceMode, horizontalMirrorEnabled, now);
        return _displayed;
    }

    internal void Reset()
    {
        _observedFrame = null;
        _lastFrameAcceptedAt = null;
        Array.Clear(_displayed);
        Array.Clear(_transitionStart);
        Array.Clear(_target);
    }

    internal static TimeSpan CalculateTransitionDuration(TimeSpan observedInterval)
    {
        var milliseconds = double.IsFinite(observedInterval.TotalMilliseconds)
            ? observedInterval.TotalMilliseconds * 0.90
            : DefaultTransitionDuration.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 8, 17));
    }

    private void Advance(DateTimeOffset now)
    {
        var progress = _transitionDuration <= TimeSpan.Zero
            ? 1
            : Math.Clamp((now - _transitionStartedAt).TotalMilliseconds /
                         _transitionDuration.TotalMilliseconds, 0, 1);
        for (var index = 0; index < _displayed.Length; index++)
        {
            var value = _transitionStart[index] + (_target[index] - _transitionStart[index]) * progress;
            _displayed[index] = float.IsFinite((float)value) ? (float)Math.Clamp(value, 0, 1) : 0;
        }
    }

    private void AcceptFrame(
        SpectrumFrame frame,
        int detailCount,
        double amplitude,
        SpectrumFrequencyBalanceMode balanceMode,
        bool horizontalMirrorEnabled,
        DateTimeOffset now)
    {
        _observedFrame = frame;
        _lastFrameAcceptedAt = now;
        _transitionStartedAt = now;
        _detailCount = detailCount;
        _amplitude = amplitude;
        _balanceMode = balanceMode;
        _horizontalMirrorEnabled = horizontalMirrorEnabled;
    }

    private void BuildTarget(
        SpectrumFrame frame,
        int detailCount,
        double amplitude,
        SpectrumFrequencyBalanceMode balanceMode,
        bool horizontalMirrorEnabled) =>
        SpectrumDisplayProcessor.ProcessInto(frame.Bands, _target, _resampleBuffer,
            balanceMode, amplitude, horizontalMirrorEnabled);

    private void EnsureCapacity(int detailCount, bool horizontalMirrorEnabled)
    {
        if (_displayed.Length != detailCount)
        {
            _displayed = new float[detailCount];
            _transitionStart = new float[detailCount];
            _target = new float[detailCount];
        }

        var resampleCount = horizontalMirrorEnabled ? detailCount / 2 : detailCount;
        if (_resampleBuffer.Length != resampleCount) _resampleBuffer = new float[resampleCount];
    }
}
