namespace Sigil.Sound.Tests;

/// <summary>
/// Plays scripted keys into a <see cref="NoteInput"/>, runs a <see cref="NoteStream"/> block by block on a host clock and records the
/// events that come out with the beat of the block they fell in. Everything is in memory and deterministic: 256-frame blocks at 48 kHz,
/// so at 120 bpm a beat is 93.75 blocks and an event lands within 0.011 of a beat of its due time.
/// </summary>
internal sealed class NoteRig : IDisposable
{
    public const int SampleRate = 48_000;
    public const int Block = 256;

    /// <summary>One event and the beat (on the host clock) of the block it came out in.</summary>
    public readonly record struct Seen(double Beat, NoteEvent Event);

    public NoteRig(double bpm = 120)
    {
        Bpm = bpm;
        Clock = new HostClock(SampleRate, bpm);
        Keys = new NoteInput { Clock = Clock };
    }

    public double Bpm { get; }

    public HostClock Clock { get; }

    public NoteInput Keys { get; }

    public NoteStreamRunner? Runner { get; private set; }

    public List<Seen> Events { get; } = [];

    /// <summary>The longest a block can be off its due time, in beats.</summary>
    public double BlockBeats => Block / (double)SampleRate * Bpm / 60.0;

    /// <summary>Builds <paramref name="stream"/> to run on this rig's clock.</summary>
    public NoteStreamRunner Run(NoteStream stream)
    {
        Runner?.Dispose();
        Runner = stream.Compile(Clock, SampleRate);
        return Runner;
    }

    /// <summary>Runs <paramref name="blocks"/> blocks, recording events and advancing the clock after each, as a host does.</summary>
    public void Render(int blocks = 1)
    {
        for (int i = 0; i < blocks; i++)
        {
            double beat = Clock.Beats;
            foreach (var e in Runner!.Process(Block))
                Events.Add(new Seen(beat, e));
            Clock.Advance(Block);
        }
    }

    /// <summary>Runs for at least <paramref name="beats"/> beats.</summary>
    public void RenderBeats(double beats) => Render((int)Math.Ceiling(beats / BlockBeats));

    /// <summary>Forgets the events recorded so far.</summary>
    public void Forget() => Events.Clear();

    public IEnumerable<Seen> Starts => Events.Where(s => s.Event.IsOn);

    public IEnumerable<Seen> Ends => Events.Where(s => !s.Event.IsOn);

    public int[] StartPitches => [.. Starts.Select(s => s.Event.Pitch)];

    /// <summary>Asserts that every note that started has ended exactly once, and none ended that had not started.</summary>
    public void AssertAllEnded()
    {
        var sounding = new HashSet<long>();
        foreach (var (beat, e) in Events)
        {
            if (e.IsOn)
                Assert.True(sounding.Add(e.Id), $"Note {e.Id} started twice without ending (beat {beat:0.###}).");
            else
                Assert.True(sounding.Remove(e.Id), $"Note {e.Id} ended without sounding (beat {beat:0.###}).");
        }

        Assert.True(sounding.Count == 0, $"{sounding.Count} notes never ended.");
    }

    public void Dispose() => Runner?.Dispose();
}
