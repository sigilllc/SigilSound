namespace Sigil.Sound;

/// <summary>Playing a note pattern live: <c>Mini.Notes("c3 e3 g3").Play(v => Saw(v.Frequency).Adsr(...))</c>.</summary>
public static class PatternPlayback
{
    /// <summary>
    /// Turns a pattern into sound. Unlike <see cref="Patterns.Play"/> on a written-out <see cref="Pattern"/>, the pattern is asked
    /// for each block as it plays, so it can run forever and change from cycle to cycle (<c>&lt;a b&gt;</c>, <c>Every</c>,
    /// <c>Degrade</c>) without being repeated, and replacing the sound or changing the tempo needs no rebuilding.
    /// </summary>
    /// <remarks>
    /// Each note lasts as long as its event in the pattern, and its velocity scales the voice. Voices are built up front and reused, so
    /// playing allocates nothing. Time is bound to the music as in <see cref="Patterns.Play"/>: with a <see cref="HostClock"/> and no
    /// <paramref name="bpm"/> the pattern follows the host's beat position and tempo, and a replacement stays on the grid; otherwise
    /// it counts from when it starts, at <paramref name="bpm"/> (120 if not given). One cycle is <see cref="Pat.BeatsPerCycle"/> beats.
    /// </remarks>
    /// <param name="pattern">The notes to play.</param>
    /// <param name="voice">Describes the sound of one note from its <see cref="Voice"/> signals. Called while the sound is built, not per note.</param>
    /// <param name="bpm">The tempo, or null to follow the host clock (or 120 without one).</param>
    /// <param name="polyphony">The most notes sounding at once, from 1 to 64. Defaults to 8.</param>
    /// <example><code>Mini.Notes("c3 [e3 g3]*2 ~ &lt;b3 d4&gt;").Every(4, p => p.Rev()).Play(v => Saw(v.Frequency).Adsr(0.003, 0.15, 0.4, 0.2))</code></example>
    public static Sound Play(this Pattern<Note> pattern, Func<Voice, Sound> voice, double? bpm = null, int polyphony = 8)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(voice);
        if (bpm is { } tempo && !(double.IsFinite(tempo) && tempo > 0)) throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "The tempo must be positive.");
        if (polyphony is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(polyphony), polyphony, "Polyphony must be between 1 and 64.");

        pattern.Validate();
        if (bpm is null)
            HostSequence.ReportFollowsHost();
        return Sound.FromOperator(ctx => new PatternPlayOperator(pattern, voice, bpm, polyphony, ctx), "Play(pattern)");
    }

    /// <summary>
    /// Plays the pattern on the left and a changed copy of it on the right, so a transformation becomes stereo width:
    /// <c>notes.Jux(p => p.Transpose(7), voice)</c> doubles every note a fifth up on the other side.
    /// </summary>
    /// <param name="pattern">The notes; played as they are, to the left.</param>
    /// <param name="transform">Makes the right-hand copy from the pattern.</param>
    /// <param name="voice">The sound of one note, as for <see cref="Play(Pattern{Note}, Func{Voice, Sound}, double?, int)"/>.</param>
    /// <param name="bpm">The tempo, or null to follow the host clock.</param>
    /// <param name="polyphony">The most notes sounding at once on each side.</param>
    /// <param name="width">How far each side is panned: 1 is hard left and right, 0 puts both in the center.</param>
    public static Sound Jux(this Pattern<Note> pattern, Func<Pattern<Note>, Pattern<Note>> transform, Func<Voice, Sound> voice,
        double? bpm = null, int polyphony = 8, double width = 1)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(transform);
        if (!(width is >= 0 and <= 1)) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be between 0 and 1.");

        var changed = transform(pattern) ?? throw new InvalidOperationException("The transform returned null.");
        return pattern.Play(voice, bpm, polyphony).Pan(-width) + changed.Play(voice, bpm, polyphony).Pan(width);
    }
}

/// <summary>
/// Plays a <see cref="Pattern{T}"/> of notes by asking it for each block's stretch of time and starting a voice at the frame of
/// every event that begins in it. The voices live in <see cref="VoicePool"/>.
/// </summary>
internal sealed class PatternPlayOperator : IOperator, IResettable, IDisposable
{
    private const double DefaultBpm = 120;

    private readonly Pattern<Note> _pattern;
    private readonly double? _bpm;
    private readonly HostClock? _clock;
    private readonly int _sampleRate;
    private readonly VoicePool _voices;
    private readonly List<PatternEvent<Note>> _events = new(64);

    private long _position;
    private bool _failed;

    // Where the last query ended; the next starts exactly there when time is continuous, so a note on a block edge is
    // played once, by the later block, however the floating-point arithmetic rounds.
    private Fraction _lastEnd;
    private double _lastEndCycles = double.NaN;

    public PatternPlayOperator(Pattern<Note> pattern, Func<Voice, Sound> factory, double? bpm, int polyphony, RenderContext context)
    {
        _pattern = pattern;
        _bpm = bpm;
        _clock = bpm is null ? context.TimeClock : null;
        _sampleRate = context.SampleRate;
        _voices = new VoicePool(factory, polyphony, context);
    }

    public void Reset()
    {
        _position = 0;
        _lastEndCycles = double.NaN;
        _failed = false;
        _voices.Reset();
    }

    public void Dispose() => _voices.Dispose();

    public void RenderBlock(Span<StereoFrame> block)
    {
        block.Clear();

        // A held host clock starts nothing new. Where the last query ended is left alone, so the pattern resumes exactly there.
        if (_clock is { Running: false })
        {
            _voices.Render(block, _position);
            _position += block.Length;
            return;
        }

        double bpm = _bpm ?? _clock?.Bpm ?? DefaultBpm;
        double cyclesPerFrame = bpm / 60.0 / _sampleRate / Pat.BeatsPerCycle;
        double blockStart = _clock is { } clock ? clock.Beats / Pat.BeatsPerCycle : _position * cyclesPerFrame;
        double blockEnd = blockStart + block.Length * cyclesPerFrame;

        var begin = Math.Abs(blockStart - _lastEndCycles) < 1e-9 ? _lastEnd : Fraction.FromDouble(blockStart);
        var end = Fraction.Max(begin, Fraction.FromDouble(blockEnd));
        _lastEnd = end;
        _lastEndCycles = blockEnd;

        _events.Clear();

        // A cycle that turns out too dense to query (the pattern passed its check when built) silences the pattern
        // until the sound is reset, rather than throwing on the audio thread. Voices already sounding finish normally.
        if (!_failed && !_pattern.TryQuery(new Arc(begin, end), _events))
            _failed = true;

        int done = 0;
        double secondsPerCycle = Pat.BeatsPerCycle * 60.0 / bpm;
        foreach (var e in _events)
        {
            if (!e.HasOnset) continue;

            int frame = (int)Math.Clamp(Math.Ceiling((e.Whole.Begin.ToDouble() - blockStart) / cyclesPerFrame - 1e-9), done, block.Length - 1);
            if (frame > done)
            {
                _voices.Render(block.Slice(done, frame - done), _position + done);
                done = frame;
            }

            var note = e.Value;
            _voices.Start(note.Frequency, note.Pitch, note.Velocity, e.Whole.Length.ToDouble() * secondsPerCycle, _position + done);
        }

        if (done < block.Length)
            _voices.Render(block[done..], _position + done);

        _position += block.Length;
    }
}
