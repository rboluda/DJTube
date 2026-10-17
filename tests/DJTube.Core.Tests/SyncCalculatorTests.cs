using DJTube.Core.Logic;
using DJTube.Core.Models;

namespace DJTube.Core.Tests;

public class SyncCalculatorTests
{
    [Fact]
    public void SameTempo_NeedsNoPitch()
    {
        var plan = Plan(new BeatGrid(128, 0), new BeatGrid(128, 0));

        Assert.NotNull(plan);
        Assert.Equal(0, plan.PitchPercent, 1e-9);
        Assert.Equal(1, plan.Multiplier);
    }

    [Fact]
    public void SlowerSlave_IsSpedUp()
    {
        var plan = Plan(new BeatGrid(128, 0), new BeatGrid(125, 0));

        Assert.NotNull(plan);
        Assert.Equal(2.4, plan.PitchPercent, 1e-9);
    }

    [Fact]
    public void MasterPitchIsTakenIntoAccount()
    {
        // Maestro a 128 con +2 % suena a 130,56.
        var plan = SyncCalculator.Plan(
            new BeatGrid(128, 0), 0, 2, false,
            new BeatGrid(130.56, 0), 0, 0, false, pitchRange: 8);

        Assert.NotNull(plan);
        Assert.Equal(0, plan.PitchPercent, 1e-9);
    }

    [Theory]
    [InlineData(64, 2)]
    [InlineData(256, 0.5)]
    [InlineData(87, 2)]
    public void HalfAndDoubleTempo_AreMatched(double slaveBpm, double expectedMultiplier)
    {
        var plan = Plan(new BeatGrid(slaveBpm == 87 ? 174 : 128, 0), new BeatGrid(slaveBpm, 0));

        Assert.NotNull(plan);
        Assert.Equal(expectedMultiplier, plan.Multiplier);
        Assert.Equal(0, plan.PitchPercent, 1e-9);
    }

    [Fact]
    public void OutOfRange_ReturnsNull()
    {
        Assert.Null(Plan(new BeatGrid(128, 0), new BeatGrid(115, 0), range: 8));
        Assert.NotNull(Plan(new BeatGrid(128, 0), new BeatGrid(115, 0), range: 16));
    }

    [Fact]
    public void PausedDecks_OnlyMatchTempo()
    {
        var plan = SyncCalculator.Plan(
            new BeatGrid(120, 0), 10.1, 0, true,
            new BeatGrid(120, 0.05), 20, 0, false, pitchRange: 8);

        Assert.NotNull(plan);
        Assert.Null(plan.SeekSeconds);
    }

    [Fact]
    public void BothPlaying_AlignsPhaseWithShortestJump()
    {
        // Maestro: fase 0,2 de beat. Esclavo: fase 0,9 → salta +0,3 beats (0,15 s), no -0,7.
        var master = new BeatGrid(120, 0);
        var slave = new BeatGrid(120, 0.05);

        var plan = SyncCalculator.Plan(master, 10.1, 0, true, slave, 20.0, 0, true, pitchRange: 8);

        Assert.NotNull(plan);
        Assert.Equal(20.15, plan.SeekSeconds!.Value, 1e-9);
        Assert.Equal(master.PhaseAt(10.1), slave.PhaseAt(plan.SeekSeconds.Value), 1e-9);
    }

    [Fact]
    public void PhaseAlignment_CompensatesOutputLatency()
    {
        // Maestro con +4 % y esclavo a velocidad normal: en 30 ms el maestro avanza más.
        var master = new BeatGrid(125, 0);
        var slave = new BeatGrid(130, 0.1);
        const double latency = 0.03;

        var plan = SyncCalculator.Plan(master, 30.0, 4, true, slave, 40.0, 0, true, pitchRange: 8, latency);

        Assert.NotNull(plan);
        double masterDecode = 30.0 + latency * 1.04;
        Assert.Equal(master.PhaseAt(masterDecode), slave.PhaseAt(plan.SeekSeconds!.Value), 1e-9);
        Assert.InRange(plan.SeekSeconds.Value - (40.0 + latency), -slave.BeatPeriod / 2, slave.BeatPeriod / 2);
    }

    [Fact]
    public void DoubleTempoSlave_AlignsToHalfBeats()
    {
        // Esclavo a 64 BPM contado a doble tempo: sus "beats" duran medio periodo (0,469 s).
        var master = new BeatGrid(128, 0);
        var slave = new BeatGrid(64, 0);

        var plan = SyncCalculator.Plan(master, 10.0, 0, true, slave, 5.0, 0, true, pitchRange: 8);

        Assert.NotNull(plan);
        double halfBeat = slave.BeatPeriod / 2;
        double slavePhase = (plan.SeekSeconds!.Value / halfBeat) % 1;
        Assert.Equal(master.PhaseAt(10.0), slavePhase, 1e-9);
    }

    private static SyncPlan? Plan(BeatGrid master, BeatGrid slave, double range = 8) =>
        SyncCalculator.Plan(master, 0, 0, false, slave, 0, 0, false, range);
}
