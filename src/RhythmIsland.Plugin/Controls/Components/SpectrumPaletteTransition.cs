using Avalonia.Media;
using RhythmIsland.Models;

namespace RhythmIsland.Controls.Components;

internal sealed class SpectrumPaletteTransition
{
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(800);
    private SpectrumPalette? _from;
    private SpectrumPalette? _target;
    private DateTimeOffset _startedAt;

    internal SpectrumPalette Resolve(SpectrumPalette target, DateTimeOffset now)
    {
        if (_target is null)
        {
            _from = target;
            _target = target;
            _startedAt = now;
            return target;
        }

        if (_target != target)
        {
            _from = ResolveCurrent(now);
            _target = target;
            _startedAt = now;
        }

        return ResolveCurrent(now);
    }

    internal void Reset()
    {
        _from = null;
        _target = null;
        _startedAt = default;
    }

    private SpectrumPalette ResolveCurrent(DateTimeOffset now)
    {
        if (_from is null || _target is null) return _target ?? _from ?? new SpectrumPalette(Colors.White, Colors.White);
        var progress = Math.Clamp((now - _startedAt).TotalMilliseconds / Duration.TotalMilliseconds, 0, 1);
        if (progress >= 1)
        {
            _from = _target;
            return _target;
        }

        return new SpectrumPalette(
            SpectrumColorHelper.Interpolate(_from.Primary, _target.Primary, progress),
            SpectrumColorHelper.Interpolate(_from.Secondary, _target.Secondary, progress));
    }
}
