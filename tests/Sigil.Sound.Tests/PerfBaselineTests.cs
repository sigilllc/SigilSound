using System.Diagnostics;
using Xunit.Abstractions;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// Timing baseline for the engine, recorded by hand in the Workbench notes (Plans/Perf Baselines/Sigil.Sound Engine Baseline.md). Does nothing unless SIGIL_PERF=1, and only means
/// something in a Release build: <c>SIGIL_PERF=1 dotnet test -c Release --filter PerfBaselineTests --logger "console;verbosity=detailed"</c>.
/// </summary>
public class PerfBaselineTests(ITestOutputHelper output)
{
    private const double Seconds = 10;

    [Fact]
    public void Record_baseline()
    {
        if (Environment.GetEnvironmentVariable("SIGIL_PERF") != "1")
            return;

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
            ("16-voice pool", Notes.Parse("C3 E3 G3 B3 D4 F4 A4 C5").Every(Beats.Sixteenth, gate: 16).Loop()
                .Play(v => Saw(v.Frequency).Adsr(0.003, 0.15, 0.6, 0.3), bpm: 120, polyphony: 16)),
        };

        MeasureSpectral();

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
