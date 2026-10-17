using DJTube.Core.Abstractions;
using DJTube.Core.Logic;

namespace DJTube.Core.Tests;

public class CueControllerTests
{
    private readonly FakeTransport _transport = new();
    private readonly CueController _cue;

    public CueControllerTests()
    {
        _cue = new CueController(_transport);
    }

    [Fact]
    public void CueWhilePlaying_ReturnsToCueAndPauses()
    {
        _cue.Reset(10);
        _transport.Play();
        _transport.PositionSeconds = 42;

        _cue.CueDown();

        Assert.False(_transport.IsPlaying);
        Assert.Equal(10, _transport.PositionSeconds);
        Assert.False(_cue.IsPreviewing);
    }

    [Fact]
    public void CueWhilePausedOffCue_SetsCuePointWithoutPlaying()
    {
        _transport.PositionSeconds = 33.5;

        _cue.CueDown();
        _cue.CueUp();

        Assert.Equal(33.5, _cue.CuePoint);
        Assert.False(_transport.IsPlaying);
        Assert.Equal(33.5, _transport.PositionSeconds);
    }

    [Fact]
    public void HoldingCueOnCuePoint_PreviewsAndReleaseReturnsToCue()
    {
        _cue.Reset(5);
        _transport.PositionSeconds = 5;

        _cue.CueDown();
        Assert.True(_transport.IsPlaying);
        Assert.True(_cue.IsPreviewing);

        _transport.PositionSeconds = 8;
        _cue.CueUp();

        Assert.False(_transport.IsPlaying);
        Assert.False(_cue.IsPreviewing);
        Assert.Equal(5, _transport.PositionSeconds);
    }

    [Fact]
    public void PositionWithinTolerance_CountsAsOnCue()
    {
        _cue.Reset(5);
        _transport.PositionSeconds = 5 + CueController.OnCueTolerance / 2;

        _cue.CueDown();

        Assert.True(_cue.IsPreviewing);
        Assert.Equal(5, _cue.CuePoint);
    }

    [Fact]
    public void PlayDuringPreview_KeepsPlayingAfterRelease()
    {
        _transport.PositionSeconds = 0;
        _cue.CueDown();

        _cue.PlayPause();
        _transport.PositionSeconds = 3;
        _cue.CueUp();

        Assert.True(_transport.IsPlaying);
        Assert.Equal(3, _transport.PositionSeconds);
    }

    [Fact]
    public void PlayPause_TogglesWhenNotPreviewing()
    {
        _cue.PlayPause();
        Assert.True(_transport.IsPlaying);

        _cue.PlayPause();
        Assert.False(_transport.IsPlaying);
    }

    [Fact]
    public void CueUpWithoutPreview_DoesNothing()
    {
        _transport.PositionSeconds = 12;

        _cue.CueUp();

        Assert.Equal(12, _transport.PositionSeconds);
        Assert.Equal(0, _transport.SeekCount);
    }

    [Fact]
    public void Reset_ClampsNegativeCuePoint()
    {
        _cue.Reset(-3);

        Assert.Equal(0, _cue.CuePoint);
    }

    private sealed class FakeTransport : IDeckTransport
    {
        public bool IsPlaying { get; private set; }

        public double PositionSeconds { get; set; }

        public int SeekCount { get; private set; }

        public void Play() => IsPlaying = true;

        public void Pause() => IsPlaying = false;

        public void Seek(double seconds)
        {
            PositionSeconds = seconds;
            SeekCount++;
        }
    }
}
