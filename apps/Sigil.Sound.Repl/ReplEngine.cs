using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.JSInterop;
using ReplHost;

namespace Sigil.Sound.Repl;

/// <summary>
/// Connects a <see cref="SoundContext"/> to Web Audio. JavaScript keeps an AudioWorklet fed by calling
/// <see cref="Render"/> whenever its buffer runs low; every new snippet is a <see cref="SoundContext.Play"/>,
/// so edits crossfade into what is already playing.
/// </summary>
/// <remarks>
/// .NET in the browser shares one thread with the page, so while Roslyn compiles nothing can render.
/// <see cref="BeforeBlockingWork"/> renders ahead by about as long as that work took last time,
/// which keeps the sound going through a compile without adding latency the rest of the time.
/// </remarks>
public sealed class ReplEngine(IJSRuntime js)
{
    private const double SafetySeconds = 0.15;
    private const double AudibleLeadSeconds = 0.06;
    private const double MinCompileLeadSeconds = 1.0;
    private const double MaxAheadSeconds = 4.0;

    private readonly Dictionary<string, double> _workEstimates = new()
    {
        ["compile"] = 0.6,
        ["analyze"] = 0.3,
        ["render"] = 0.5,
    };

    private SoundContext? _output;
    private HostClock? _noteClock;
    private StereoFrame[] _frames = [];

    /// <summary>Raised when the playing sound throws while rendering; playback falls back to silence.</summary>
    public event Action<string>? RenderFailed;

    public int SampleRate => _output?.RenderContext.SampleRate ?? RenderContext.DefaultSampleRate;

    public bool IsRunning => _output is not null;

    /// <summary>The sound passed to the last <see cref="Play"/>, if any.</summary>
    public Sound? Current { get; private set; }

    private IJSInProcessRuntime Js => (IJSInProcessRuntime)js;

    /// <summary>Starts Web Audio (call from a click or key press) and the output it pulls from.</summary>
    public async Task StartAsync(DotNetObjectReference<ReplEngine> self)
    {
        int sampleRate = await js.InvokeAsync<int>("sigilRepl.audio.start", self);
        // The clock of the notes: streams measure their echoes and chord boundaries on the audio that is rendered, not on the wall clock,
        // so a key, an echo and a pattern played as notes keep time with each other. The context advances it after every block it
        // renders (a call renders up to seconds of audio in many blocks, so advancing it once per call would show every block the same beat).
        if (_noteClock is null || _noteClock.SampleRate != sampleRate)
        {
            _noteClock = new HostClock(sampleRate);
            ReplApi.Keys.Clock = _noteClock;
        }

        if (_output is null || _output.RenderContext.SampleRate != sampleRate)
            _output = new SoundContext(sampleRate, fadeSeconds: 0.03, clock: _noteClock);
    }

    public void Play(Sound sound)
    {
        Current = sound;
        if (_output is null)
            return;

        // Audio rendered ahead belongs to the old sound; drop most of it so the change is heard now.
        Js.InvokeVoid("sigilRepl.audio.flush", AudibleLeadSeconds);

        // A pattern that follows the clock starts from its first note when you press Run, as it did before the sounds had a clock.
        _noteClock?.Seek(0, 0);
        _output.Play(sound);
    }

    public void Stop()
    {
        Current = null;
        _output?.Stop(fadeSeconds: 0.08);
    }

    /// <summary>
    /// Runs <paramref name="work"/> (a compile, a completion request) after topping up the audio buffer
    /// so playback continues while the page's only thread is busy.
    /// </summary>
    public T BeforeBlockingWork<T>(string kind, Func<T> work)
    {
        double estimate = _workEstimates[kind];
        // A first compile of something unusual can take far longer than the last one did, so compiles
        // always start with a second of audio queued; Play then flushes whatever the new sound doesn't need.
        double lead = estimate * 1.5 + SafetySeconds;
        if (kind == "compile")
            lead = Math.Max(lead, MinCompileLeadSeconds);
        TopUp(Math.Min(MaxAheadSeconds, lead));

        var timer = Stopwatch.StartNew();
        try
        {
            return work();
        }
        finally
        {
            // Follow increases at once and decreases slowly, so one quick run doesn't cause a dropout.
            double took = timer.Elapsed.TotalSeconds;
            _workEstimates[kind] = took > estimate ? took : estimate * 0.8 + took * 0.2;
        }
    }

    /// <summary>Renders <paramref name="frames"/> interleaved float32 frames as bytes. Called by the audio pump.</summary>
    [JSInvokable]
    public byte[] Render(int frames)
    {
        if (_output is null || frames <= 0)
            return [];

        frames = Math.Min(frames, (int)(MaxAheadSeconds * SampleRate));
        if (_frames.Length < frames)
            _frames = new StereoFrame[frames];
        var block = _frames.AsSpan(0, frames);

        ExecutionGuard.Reset(frames);

        try
        {
            _output.Render(block);
        }
        catch (Exception e)
        {
            // The running graph is broken; start over from silence.
            _output = new SoundContext(SampleRate, fadeSeconds: 0.03, clock: _noteClock);
            Current = null;
            block.Clear();
            RenderFailed?.Invoke($"{e.GetType().Name}: {e.Message}");
        }

        // Protect ears and speakers from runaway snippets: no NaN, nothing past full scale.
        var samples = MemoryMarshal.Cast<StereoFrame, float>(block);
        // Almost every sample is in range, so that is the only test they pay for (this loop runs on the interpreter).
        for (int i = 0; i < samples.Length; i++)
        {
            float s = samples[i];
            if (s >= -1f && s <= 1f)
                continue;
            samples[i] = float.IsFinite(s) ? Math.Clamp(s, -1f, 1f) : 0f;
        }

        // The notes this block sent out, for the strip. The strip places them at the frame of the audio timeline this block starts at.
        if (NoteTrace.Page.HasPending)
            Js.InvokeVoid("sigilRepl.notes.add", NoteTrace.Page.Drain());

        return MemoryMarshal.AsBytes(samples).ToArray();
    }

    private void TopUp(double seconds)
    {
        if (_output is null)
            return;
        int buffered = Js.Invoke<int>("sigilRepl.audio.buffered");
        int missing = (int)(seconds * SampleRate) - buffered;

        // Push in small chunks: the worklet receives each one on its own thread right away, so a heavy sound
        // that renders at half real time still outpaces playback instead of starving it while one big chunk renders.
        int chunk = SampleRate / 20;
        while (missing > 0)
        {
            int frames = Math.Min(chunk, missing);
            Js.InvokeVoid("sigilRepl.audio.push", Render(frames));
            missing -= frames;
        }
    }
}
