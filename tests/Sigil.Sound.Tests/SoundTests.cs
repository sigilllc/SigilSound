using Sigil.Sound.Testing;
using Sigil.Sound.Wav;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class SoundTests
{
    [Theory]
    [InlineData(440, 48_000)]
    [InlineData(1000, 44_100)]
    public void Sine_renders_expected_frequency_and_amplitude(double hz, int sampleRate)
    {
        var audio = Sine(hz).Render(1.0, sampleRate);

        AudioAssert.FrequencyNear(audio, hz, 0.5);
        AudioAssert.PeakNear(audio, 1.0f, 0.001f);
        AudioAssert.RmsNear(audio, 1 / Math.Sqrt(2), 0.005);
    }

    [Fact]
    public void Gain_scales_amplitude()
    {
        var audio = (Sine(220) * 0.25).Render(0.5);

        AudioAssert.PeakNear(audio, 0.25f, 0.001f);
    }

    [Fact]
    public void Saw_and_square_render_their_frequency()
    {
        AudioAssert.FrequencyNear(Saw(110).LowPass(2000).Render(1.0), 110, 1.0);
        AudioAssert.FrequencyNear(Square(330).Render(1.0), 330, 1.0);
    }

    [Fact]
    public void Sound_is_an_immutable_reusable_description()
    {
        var sound = Saw(110).LowPass(cutoff: Sine(2).Range(300, 3000), q: 1.2) + Noise(seed: 7) * 0.1;

        var first = sound.Render(0.25).ToInterleaved();
        var second = sound.Render(0.25).ToInterleaved();

        Assert.Equal(first, second);
    }

    [Fact]
    public void LowPass_attenuates_noise()
    {
        double raw = SignalAnalysis.Rms(Noise().Render(0.5).ToMono());
        double filtered = SignalAnalysis.Rms(Noise().LowPass(200).Render(0.5).ToMono());

        Assert.True(filtered < raw * 0.2, $"raw {raw}, filtered {filtered}");
    }

    [Fact]
    public void Adsr_ends_in_silence_after_release()
    {
        var audio = Sine(440).Adsr(0.01, 0.1, 0.5, 0.2, hold: 0.3).Render(1.0);

        AudioAssert.PeakNear(audio, 1.0f, 0.01f);
        AudioAssert.IsSilent(audio.ToMono().AsSpan(audio.SampleRate / 2));
    }

    [Fact]
    public void Fn_turns_a_function_of_time_into_a_param()
    {
        var steady = Sine(Fn(_ => 440)).Render(1.0);
        var gated = Sine(440) * Fn(t => t < 0.5 ? 1 : 0);

        AudioAssert.FrequencyNear(steady, 440, 0.5);
        AudioAssert.IsSilent(gated.Render(1.0).ToMono().AsSpan(24_001));
    }

    [Fact]
    public void RenderBlock_allocates_zero_bytes()
    {
        var sound = (Saw(Sine(5).Range(100, 120)).LowPass(Sine(0.5).Range(400, 4000), 0.9).Adsr(0.01, 0.1, 0.7, 0.3)
                     + Square(220, width: 0.3) * Sine(3) + Noise() * 0.05) * 0.5;
        var context = RenderContext.Default;
        var op = sound.Compile(context);
        var block = new StereoFrame[context.BlockSize];

        // Warm up so JIT and tiering don't count against the measurement.
        for (int i = 0; i < 200; i++)
            op.RenderBlock(block);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            op.RenderBlock(block);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void RenderBlock_accepts_partial_blocks()
    {
        var op = Sine(440).Compile(RenderContext.Default);
        var block = new StereoFrame[17];

        op.RenderBlock(block);

        Assert.Equal(0f, block[0].Left);
        Assert.NotEqual(0f, block[16].Left);
    }

    [Theory]
    [InlineData(WavFormat.Pcm16, 2)]
    [InlineData(WavFormat.Pcm24, 3)]
    [InlineData(WavFormat.Float32, 4)]
    public void Wav_has_valid_header_and_size(WavFormat format, int bytesPerSample)
    {
        using var stream = new MemoryStream();

        Sine(440).RenderToWav(stream, 0.1, 44_100, format);

        var bytes = stream.ToArray();
        int frames = 4410;
        Assert.Equal(44 + frames * 2 * bytesPerSample, bytes.Length);
        Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
        Assert.Equal("WAVE"u8.ToArray(), bytes[8..12]);
        Assert.Equal(44_100, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(bytesPerSample * 8, BitConverter.ToInt16(bytes, 34));
    }

    [Fact]
    public void Compile_rejects_invalid_context()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sine(440).Compile(new RenderContext(0)));
    }
}
