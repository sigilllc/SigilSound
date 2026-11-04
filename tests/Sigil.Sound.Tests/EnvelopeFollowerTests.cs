using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class EnvelopeFollowerTests
{
    private const int SampleRate = Measure.SampleRate;

    private static float At(float[] samples, double seconds) => samples[(int)(seconds * SampleRate)];

    [Fact]
    public void Attack_covers_63_percent_of_a_step_in_one_time_constant()
    {
        var step = Fn(t => t < 0.2 ? 0.0 : 1.0);

        var samples = Measure.Mono(step.FollowEnvelope(attack: 0.01, release: 0.1), 0.5);

        Assert.InRange(At(samples, 0.21), 0.62f, 0.65f);
        Assert.InRange(At(samples, 0.25), 0.98f, 1.0f);
    }

    [Fact]
    public void Release_falls_to_37_percent_in_one_time_constant()
    {
        // The input is at full level until 0.2 s, so the follower has already reached 1 by then.
        var step = Fn(t => t < 0.2 ? 1.0 : 0.0);

        var samples = Measure.Mono(step.FollowEnvelope(attack: 0.01, release: 0.1), 0.6);

        Assert.InRange(At(samples, 0.19), 0.99f, 1.0f);
        Assert.InRange(At(samples, 0.3), 0.35f, 0.39f);
    }

    [Fact]
    public void Output_is_a_unipolar_level_that_tracks_the_peaks()
    {
        // A 100 Hz wave has a peak every 10 ms, so the follower needs an attack well under that to reach each peak.
        var samples = Measure.Mono((Sine(100) * 0.8).FollowEnvelope(attack: 0.001, release: 0.05), 1.0);

        Assert.All(samples, s => Assert.InRange(s, 0f, 1f));
        Assert.InRange(Measure.Peak(Measure.Slice(samples, 0.5, 1.0)), 0.76f, 0.82f);
    }
}
