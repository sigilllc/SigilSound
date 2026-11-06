using Sigil.Sound.Testing;

namespace Sigil.Sound.Repl.Tests;

/// <summary>
/// The "Church organ" example claims 64 sines from rotating phasors: on pitch, stable for as long as it plays,
/// silent above Nyquist and restartable. Its own definition is reused here, only the final expression is swapped.
/// </summary>
public class PhasorOrganTests
{
    private const int Rate = RenderContext.DefaultSampleRate;

    // Everything of the example up to the tune: the Organ function, ending where `var line` begins.
    private static string OrganDefinition()
    {
        var code = Examples.All.Single(e => e.Name == "Church organ").Code;
        var end = code.IndexOf("var line =", StringComparison.Ordinal);
        Assert.True(end > 0, "the example no longer has a `var line =` after the Organ function");
        return code[..end];
    }

    private static Sound Organ(string frequency) => SnippetCompiler.Compile(OrganDefinition() + $"Organ({frequency})").Run();

    [Fact]
    public void It_sounds_the_pitch_it_is_given_and_keeps_it_for_thirty_seconds()
    {
        var audio = HostRender.Render(Organ("220"), 30);
        var mono = audio.ToMono();

        var last = mono.AsSpan(mono.Length - Rate);
        Assert.InRange(SignalAnalysis.PeakFrequency(last, Rate), 215, 225);
    }

    [Fact]
    public void Its_level_does_not_drift_because_of_rounding()
    {
        var mono = HostRender.Render(Organ("220"), 30).ToMono();

        // The gains sum to 1 and every phasor stays on the unit circle, so the sum can never pass 1.
        var peak = SignalAnalysis.Peak(mono);
        Assert.InRange(peak, 0.5f, 1.0001f);
    }

    [Fact]
    public void Partials_past_nyquist_are_left_out_instead_of_aliasing()
    {
        var audio = HostRender.Render(Organ("9000"), 1);   // 4.5 kHz is the 16' pipe here: five partials fit under 24 kHz

        AudioAssert.IsFinite(audio);
        Assert.True(SignalAnalysis.Peak(audio.ToMono()) > 0.1f);
        Assert.InRange(SignalAnalysis.PeakFrequency(audio.ToMono(), Rate), 8_900, 9_100);
    }

    [Fact]
    public void A_moving_pitch_is_followed_without_clicks()
    {
        var audio = HostRender.Render(Organ("Sine(5).Range(215, 225)"), 2);
        var mono = audio.ToMono();

        AudioAssert.IsFinite(audio);
        Assert.InRange(SignalAnalysis.Peak(mono), 0.5f, 1.0001f);

        // The phasors keep their phase when the rotation changes, so the waveform has no jump between samples.
        var largestStep = 0f;
        for (int i = 1; i < mono.Length; i++)
            largestStep = Math.Max(largestStep, Math.Abs(mono[i] - mono[i - 1]));
        Assert.True(largestStep < 0.5f, $"a jump of {largestStep} between two samples");
    }

    [Fact]
    public void Reset_starts_the_voice_over_exactly()
    {
        var context = new RenderContext(Rate);
        var op = Organ("330").Compile(context);

        StereoFrame[] Blocks(int count)
        {
            var frames = new StereoFrame[count * context.BlockSize];
            for (int i = 0; i < count; i++)
            {
                var block = frames.AsSpan(i * context.BlockSize, context.BlockSize);
                ExecutionGuard.Reset(block.Length);
                op.RenderBlock(block);
            }

            return frames;
        }

        var first = Blocks(4);
        Blocks(8);                               // play on, so there is state to clear
        ((IResettable)op).Reset();
        var again = Blocks(4);

        Assert.Equal(first, again);
    }
}
