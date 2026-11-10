using Sigil.Sound;

namespace Sigil.Sound.Repl;

/// <summary>
/// The notes a snippet sends out with <c>Out.Notes</c>, kept for the page to draw as a strip. The page drains it after every block it renders.
/// </summary>
/// <remarks>
/// Ids here are the trace's own, so two sounds alive at once during a crossfade cannot mix up each other's notes. Nothing here is
/// reachable from a snippet: it is internal, and the page and <see cref="NoteOutTap"/> are the only ones that touch it.
/// </remarks>
internal sealed class NoteTrace
{
    private readonly List<double> _pending = [];
    private int _next;

    /// <summary>The one the page draws.</summary>
    public static NoteTrace Page { get; } = new();

    public bool HasPending => _pending.Count > 0;

    /// <summary>Records that a note started and returns the id to end it with.</summary>
    public int Begin(int pitch, float velocity)
    {
        int id = ++_next;
        _pending.AddRange([0, id, pitch, velocity]);
        return id;
    }

    /// <summary>Records that the note with this id ended.</summary>
    public void End(int id) => _pending.AddRange([1, id, 0, 0]);

    /// <summary>The events since the last call, as the flat array the page's strip takes: kind (0 starts, 1 ends), id, pitch, velocity.</summary>
    public double[] Drain()
    {
        var events = _pending.ToArray();
        _pending.Clear();
        return events;
    }
}

/// <summary>
/// Plays the notes of a stream with the page's default voice and records each one in the <see cref="NoteTrace"/>, so the strip beside
/// the oscilloscope shows what the snippet sends out. This is what the device's Note Out would carry, heard through one voice because
/// the page has no device to send it to.
/// </summary>
/// <remarks>
/// Notes keep their identity: a note's end ends that note even when several share a pitch, and a note with cents sounds at its exact
/// pitch. When the sound is replaced or stopped, everything still sounding is ended, in the voice and in the trace.
/// </remarks>
internal sealed class NoteOutTap : IOperator, IResettable, IDisposable
{
    private readonly NoteStreamRunner _runner;
    private readonly NoteTrace _trace;
    private readonly NoteInput _voices = new();
    private readonly IOperator _voiceOperator;
    private readonly Dictionary<long, (PlayingNote Note, int Trace)> _sounding = [];

    /// <param name="clock">The page's note clock, so a pattern turned into notes and the chord of the keyboard keep one time; null counts for itself.</param>
    public NoteOutTap(NoteStream stream, Func<Voice, Sound> voice, NoteTrace trace, HostClock? clock, RenderContext context)
    {
        _trace = trace;
        _runner = stream.Compile(clock, context.SampleRate);
        _voiceOperator = _voices.Play(voice, polyphony: 8).Compile(context);
    }

    public void Reset()
    {
        EndAll(_runner.Reset());
        (_voiceOperator as IResettable)?.Reset();
    }

    public void Dispose()
    {
        EndAll(_runner.Reset());
        _runner.Dispose();
        (_voiceOperator as IDisposable)?.Dispose();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        foreach (var e in _runner.Process(block.Length))
            Apply(e);
        _voiceOperator.RenderBlock(block);
    }

    private void EndAll(ReadOnlySpan<NoteEvent> ends)
    {
        foreach (var e in ends)
            Apply(e);
        _sounding.Clear();
    }

    private void Apply(in NoteEvent e)
    {
        if (e.IsOn)
        {
            var note = _voices.Start(e.Pitch, Math.Clamp(e.Velocity, 1f / 127f, 1f));
            note.Bend = e.Cents / 100.0;
            _sounding[e.Id] = (note, _trace.Begin(e.Pitch, e.Velocity));
        }
        else if (_sounding.Remove(e.Id, out var playing))
        {
            playing.Note.Release();
            _trace.End(playing.Trace);
        }
    }
}
