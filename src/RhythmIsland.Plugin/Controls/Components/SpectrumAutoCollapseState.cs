using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Controls.Components;

internal sealed class SpectrumAutoCollapseState
{
    private static readonly TimeSpan AudibleFrameFreshness = TimeSpan.FromMilliseconds(250);
    private DateTimeOffset? _silenceStartedAt;

    internal bool IsCollapsed { get; private set; }

    internal bool Update(SpectrumFrame? frame, SpectrumComponentSettings settings,
        DateTimeOffset now, bool isEditMode)
    {
        if (isEditMode || !settings.AutoCollapseEnabled)
        {
            _silenceStartedAt = null;
            IsCollapsed = false;
            return false;
        }

        var isAudible = frame is not null
                        && !frame.IsSilent
                        && SpectrumDisplayProcessor.HasVisibleSignal(
                            frame.Bands,
                            settings.BarCount,
                            settings.FrequencyBalanceMode,
                            settings.Amplitude,
                            settings.HorizontalMirrorEnabled)
                        && now - frame.GeneratedAt <= AudibleFrameFreshness;
        if (isAudible)
        {
            _silenceStartedAt = null;
            IsCollapsed = false;
            return false;
        }

        _silenceStartedAt ??= now;
        IsCollapsed = now - _silenceStartedAt.Value >=
                      TimeSpan.FromSeconds(settings.SilenceCollapseDelaySeconds);
        return IsCollapsed;
    }

    internal void Reset()
    {
        _silenceStartedAt = null;
        IsCollapsed = false;
    }
}
