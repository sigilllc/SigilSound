using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class TapeDelayTests
{
    private const int SampleRate = Measure.SampleRate;

    // One millisecond of full level, then silence.
    private static Sound Impulse => Fn(t => t < 0.001 ? 1.0 : 0.0);

    private static int IndexOfPeak(float[] samples, double fromSeconds, double toSeconds)
    {
        int start = (int)(fromSeconds * SampleRate);
        var window = Measure.Slice(samples, fromSeconds, toSeconds);
        return start + Array.IndexOf(window, window.MaxBy(MathF.Abs));
    }

    // The largest change between two neighbouring samples in a window: a click shows up as a large jump.
    private static float LargestJump(float[] samples, double fromSeconds, double toSeconds)
    {
        var window = Measure.Slice(samples, fromSeconds, toSeconds);
        float largest = 0;
        for (int i = 1; i < window.Length; i++)
            largest = MathF.Max(largest, MathF.Abs(window[i] - window[i - 1]));
        return largest;
    }

    [Fact]
    public void Repeats_arrive_at_the_delay_time()
    {
        var samples = Measure.Mono(Impulse.TapeDelay(time: 0.1, feedback: 0, damping: 20_000, drive: 1, mix: 1), 0.3);

        Assert.InRange(IndexOfPeak(samples, 0.095, 0.15), (int)(0.099 * SampleRate), (int)(0.101 * SampleRate));
    }

    [Fact]
    public void Changing_the_time_glides_the_pitch_instead_of_clicking()
    {
        // The delay jumps from 100 ms to 200 ms at 0.5 s. A plain delay reads a different part of the tone at once,
        // which is a click; the tape head covers the same distance over tens of milliseconds, which is a glide.
        // The tone is 445.5 Hz so that the switch at 0.5 s falls on a sine peak, and the 0.1 s change in delay
        // reads about half a cycle away. That makes the step as large as it can be, so its absence in the tape is meaningful.
        var tone = Sine(445.5) * 0.5;
        var time = Fn(t => t < 0.5 ? 0.1 : 0.2);

        var tape = Measure.Mono(tone.TapeDelay(time: time, feedback: 0, damping: 20_000, drive: 1, mix: 1), 1.0);
        var plain = Measure.Mono(tone.Delay(time, feedback: 0, mix: 1), 1.0);

        Assert.True(LargestJump(tape, 0.45, 0.8) < 0.06f, $"Tape jump was {LargestJump(tape, 0.45, 0.8)}.");
        Assert.True(LargestJump(plain, 0.45, 0.8) > 0.3f, $"Plain delay jump was {LargestJump(plain, 0.45, 0.8)}; the contrast needs it to click.");
    }

    [Fact]
    public void Feedback_at_high_drive_decays_and_stays_finite()
    {
        var audio = Impulse.TapeDelay(time: 0.05, feedback: 0.95, drive: 4, mix: 0.5, damping: 3000).Render(4.0);

        AudioAssert.IsFinite(audio);
        var samples = audio.ToMono();
        Assert.True(Measure.Rms(Measure.Slice(samples, 3.0, 4.0)) < Measure.Rms(Measure.Slice(samples, 0.05, 0.5)) * 0.5,
            "The repeats must die away, not build up.");
    }

    // A click on the left channel only. The right channel stays silent, so any sound there came from the feedback.
    private static Sound LeftClick => Sound.FromOperator(_ =>
    {
        long frame = 0;
        return Operator.From(block =>
        {
            for (int i = 0; i < block.Length; i++)
                block[i] = new StereoFrame(frame++ < 48 ? 1f : 0f, 0f);
        });
    });

    [Fact]
    public void Ping_pong_bounces_each_echo_to_the_other_channel()
    {
        var audio = LeftClick.TapeDelay(time: 0.1, feedback: 0.8, damping: 20_000, drive: 1, mix: 1, pingPong: true).Render(0.4);

        // The first echo lands on the left, the second on the right, the third back on the left.
        Assert.True(Measure.Peak(Measure.Slice(audio.Left(), 0.09, 0.12)) > 0.5f);
        AudioAssert.IsSilent(Measure.Slice(audio.Right(), 0.09, 0.12));
        Assert.True(Measure.Peak(Measure.Slice(audio.Right(), 0.19, 0.22)) > 0.5f);
        AudioAssert.IsSilent(Measure.Slice(audio.Left(), 0.19, 0.22));
        Assert.True(Measure.Peak(Measure.Slice(audio.Left(), 0.29, 0.32)) > 0.3f);
    }

    [Fact]
    public void Without_ping_pong_the_echoes_stay_on_the_left()
    {
        var audio = LeftClick.TapeDelay(time: 0.1, feedback: 0.8, damping: 20_000, drive: 1, mix: 1).Render(0.4);

        AudioAssert.IsSilent(Measure.Slice(audio.Right(), 0.0, 0.4));
    }

    [Fact]
    public void Drive_below_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Impulse.TapeDelay(time: 0.1, drive: 0.5));
    }

    [Fact]
    public void A_repeat_passes_through_the_feedback_at_exactly_its_set_level()
    {
        // With the low-pass wide open, a second echo is the first times the feedback, times the one-pole's peak gain (c).
        // The saturator must not add or remove anything at small levels, so the ratio is exact to a fraction of a percent.
        const double cutoff = 21_600;
        double c = 1 - Math.Exp(-2 * Math.PI * cutoff / SampleRate);
        // Exactly one frame at 1e-4: a single sample through a one-pole filter peaks at c, which is what the ratio relies on.
        var clicks = Fn(t => t < 0.5 / SampleRate ? 1e-4 : 0.0);

        var audio = clicks.TapeDelay(time: 0.1, feedback: 0.5, damping: cutoff, drive: 1, mix: 1).Render(0.3).ToMono();

        double first = Measure.Peak(Measure.Slice(audio, 0.095, 0.11));
        double second = Measure.Peak(Measure.Slice(audio, 0.195, 0.21));
        Assert.InRange(second / first, 0.5 * c * 0.995, 0.5 * c * 1.005);
    }

    [Fact]
    public void Lower_damping_darkens_the_later_repeats()
    {
        // A short click, so 4 kHz is not a spectral null of the pulse. Three trips round the loop with a 500 Hz low-pass
        // leave almost no 4 kHz; with the low-pass open the repeats keep it.
        var click = Fn(t => t < 0.0001 ? 1.0 : 0.0);
        var dark = Measure.Mono(click.TapeDelay(time: 0.05, feedback: 0.8, damping: 500, drive: 1, mix: 1), 0.3);
        var bright = Measure.Mono(click.TapeDelay(time: 0.05, feedback: 0.8, damping: 20_000, drive: 1, mix: 1), 0.3);

        double darkTop = Measure.Power(Measure.Slice(dark, 0.14, 0.2), 4000);
        double brightTop = Measure.Power(Measure.Slice(bright, 0.14, 0.2), 4000);
        Assert.True(darkTop < brightTop * 0.1, $"Dark repeats at 4 kHz: {darkTop}; bright: {brightTop}.");
    }
}
