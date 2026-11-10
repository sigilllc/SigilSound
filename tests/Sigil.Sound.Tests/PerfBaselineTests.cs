using System.Diagnostics;
using Xunit.Abstractions;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// Timing baseline for the engine, recorded by hand in the maintainers' notes. Does nothing unless SIGIL_PERF=1, and only means
/// something in a Release build: <c>SIGIL_PERF=1 dotnet test -c Release --filter PerfBaselineTests --logger "console;verbosity=detailed"</c>.
/// </summary>
public class PerfBaselineTests(ITestOutputHelper output)
{
    private const double Seconds = 10;

    [EnvironmentFact("SIGIL_PERF")]
    public void Record_baseline()
    {
        var cases = new (string Name, Sound Sound)[]
        {
            ("saw", Saw(110)),
            ("saw lowpass, sine LFO cutoff", Saw(110).LowPass(Sine(0.5).Range(200, 4000))),
            ("saw lowpass, control cutoff", Saw(110).LowPass(new Control(1000, 20, 18000).Map(200, 4000))),
            ("saw lowpass, Fn cutoff per sample", Saw(110).LowPass(Fn(t => 1000 + 500 * Math.Sin(t * 3)))),
            ("saw lowpass, Fn cutoff 1 ms", Saw(110).LowPass(Fn(t => 1000 + 500 * Math.Sin(t * 3), FunctionRate.PerMillisecond))),
            ("saw reverb", Saw(110).Reverb()),
            ("saw fdn reverb", Saw(110).FdnReverb()),
            ("saw spectral pitch shift +7", Saw(110).PitchShift(7)),
            ("saw convolve 0.5 s stereo response", Saw(110).Convolve(Response(0.5, stereo: true))),
            ("saw convolve 2 s mono response", Saw(110).Convolve(Response(2, stereo: false))),
            ("saw convolve 10 s stereo response", Saw(110).Convolve(Response(10, stereo: true))),
            ("16-voice pool", Notes.Parse("C3 E3 G3 B3 D4 F4 A4 C5").Every(Beats.Sixteenth, gate: 16).Loop()
                .Play(v => Saw(v.Frequency).Adsr(0.003, 0.15, 0.6, 0.3), bpm: 120, polyphony: 16)),
        };

        MeasureSpectral();
        MeasureConvolutionWorstBlock();

        foreach (var (name, sound) in cases)
        {
            var op = sound.Compile(RenderContext.Default);
            var block = new StereoFrame[RenderContext.DefaultBlockSize];
            for (int i = 0; i < 200; i++) op.RenderBlock(block);

            int blocks = (int)(Seconds * RenderContext.DefaultSampleRate / block.Length);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < blocks; i++) op.RenderBlock(block);
            watch.Stop();

            double nsPerFrame = watch.Elapsed.TotalMilliseconds * 1e6 / (blocks * block.Length);
            output.WriteLine($"{name,-40} {nsPerFrame,8:0.0} ns/frame  {Seconds / watch.Elapsed.TotalSeconds,8:0}x realtime");
        }
    }

    // A decaying noise burst: what a recorded room looks like to the convolver.
    private static AudioBuffer Response(double seconds, bool stereo)
    {
        var random = new Random(3);
        var frames = new StereoFrame[(int)(seconds * RenderContext.DefaultSampleRate)];
        for (int i = 0; i < frames.Length; i++)
        {
            float fade = MathF.Exp(-6f * i / frames.Length);
            float left = (float)(random.NextDouble() * 2 - 1) * fade;
            frames[i] = new StereoFrame(left, stereo ? (float)(random.NextDouble() * 2 - 1) * fade : left);
        }
        return new AudioBuffer(frames, RenderContext.DefaultSampleRate);
    }

    // The average hides the FFT boundaries; a block that runs long is the one that glitches. A 256 frame block has 5.3 ms.
    private void MeasureConvolutionWorstBlock()
    {
        foreach (var (name, seconds) in new[] { ("0.5 s", 0.5), ("2 s", 2.0), ("10 s", 10.0) })
        {
            var op = Saw(110).Convolve(Response(seconds, stereo: true)).Compile(RenderContext.Default);
            var block = new StereoFrame[RenderContext.DefaultBlockSize];
            for (int i = 0; i < 400; i++) op.RenderBlock(block);

            double worst = 0, total = 0;
            const int blocks = 4000;
            for (int i = 0; i < blocks; i++)
            {
                long start = Stopwatch.GetTimestamp();
                op.RenderBlock(block);
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                worst = Math.Max(worst, ms);
                total += ms;
            }
            output.WriteLine($"{"convolve " + name + " stereo, per block",-40} mean {total / blocks,6:0.000} ms  worst {worst,6:0.000} ms  (budget 5.333 ms)");
        }
    }

    private void MeasureSpectral()
    {
        foreach (int size in new[] { 1024, 2048, 4096 })
        {
            var plan = new Spectral.FftPlan(size);
            var re = new float[size];
            var im = new float[size];
            for (int i = 0; i < size; i++) re[i] = MathF.Sin(i * 0.1f);
            for (int i = 0; i < 2000; i++) { plan.Forward(re, im); plan.Inverse(re, im); }

            const int rounds = 20_000;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < rounds; i++) { plan.Forward(re, im); plan.Inverse(re, im); }
            watch.Stop();
            output.WriteLine($"{"fft " + size + " forward+inverse",-40} {watch.Elapsed.TotalMilliseconds * 1000 / rounds,8:0.0} us/pair");
        }

        var stft = new Spectral.StftProcessor(2048, 512, (_, _) => { });
        var input = new float[48_000];
        var result = new float[48_000];
        for (int i = 0; i < 5; i++) stft.Process(input, result);
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) stft.Process(input, result);
        timer.Stop();
        output.WriteLine($"{"stft 2048/512 identity, mono",-40} {timer.Elapsed.TotalMilliseconds * 1e6 / (100 * 48_000),8:0.0} ns/frame");
    }
}
