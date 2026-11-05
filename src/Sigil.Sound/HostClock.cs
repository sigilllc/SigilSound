namespace Sigil.Sound;

/// <summary>
/// The host's notion of "now": how many frames it has rendered and where it is on a musical grid. Put one on
/// <see cref="RenderContext.Clock"/> and time no longer starts over each time a sound is compiled, which is what keeps a
/// live-coded LFO in phase and a looping pattern on the beat when the sound under them is swapped for another.
/// </summary>
/// <remarks>
/// <para>
/// Without a clock every sound counts time from its own first frame, which is right for rendering a file and for one-shot
/// demos. With one, these follow the clock instead: <see cref="Sounds.Fn(Func{double, double})"/> and function parameters read host time,
/// sines, saws and pulses with a constant frequency continue the phase they would have had, and
/// <c>Pattern.Play</c> without an explicit tempo follows <see cref="Bpm"/> and <see cref="Beats"/>.
/// Notes played by a voice keep their own time, starting at zero with each note.
/// </para>
/// <para>
/// The renderer owns the clock: it calls <see cref="Advance"/> after rendering each block (a <see cref="SoundContext"/> given
/// a clock does this itself) and <see cref="Seek"/> when the transport jumps. Only <see cref="Bpm"/> may be changed from
/// another thread. Reading the clock is real-time safe.
/// </para>
/// </remarks>
public sealed class HostClock
{
    private long _samplePosition;
    private double _beats;
    private double _bpm;
    private int _running = 1;

    /// <param name="sampleRate">Frames per second; must equal the sample rate of every <see cref="RenderContext"/> that uses the clock.</param>
    /// <param name="bpm">Beats (quarter notes) per minute. Defaults to 120.</param>
    public HostClock(int sampleRate, double bpm = 120)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        ValidateTempo(bpm);
        SampleRate = sampleRate;
        _bpm = bpm;
    }

    /// <summary>Frames per second the clock counts in.</summary>
    public int SampleRate { get; }

    /// <summary>The frame number of the first frame of the block being rendered.</summary>
    public long SamplePosition => Volatile.Read(ref _samplePosition);

    /// <summary>Seconds at <see cref="SamplePosition"/>.</summary>
    public double Seconds => (double)SamplePosition / SampleRate;

    /// <summary>The musical position, in beats, of the first frame of the block being rendered.</summary>
    public double Beats => Volatile.Read(ref _beats);

    /// <summary>The tempo in beats per minute. Takes effect from the next block; safe to set from any thread.</summary>
    public double Bpm
    {
        get => Volatile.Read(ref _bpm);
        set
        {
            ValidateTempo(value);
            Volatile.Write(ref _bpm, value);
        }
    }

    /// <summary>
    /// Whether the musical position moves. A host sets it to false while its transport is paused or stopped: <see cref="Beats"/> then
    /// stays where it is while <see cref="SamplePosition"/> keeps counting, so sounds that read host time (a live-coded LFO, an
    /// oscillator's phase) are not disturbed. <c>Pattern.Play</c> starts no new notes while the clock is held and carries on from the same
    /// beat, without repeating or skipping one, when it runs again. Notes already sounding finish. Safe to set from any thread.
    /// </summary>
    public bool Running
    {
        get => Volatile.Read(ref _running) != 0;
        set => Volatile.Write(ref _running, value ? 1 : 0);
    }

    /// <summary>
    /// Moves the clock past a rendered block of <paramref name="frames"/>, advancing the beat position at the current tempo
    /// (unless <see cref="Running"/> is false, which advances only the sample position).
    /// </summary>
    public void Advance(int frames)
    {
        if (frames < 0)
            throw new ArgumentOutOfRangeException(nameof(frames), frames, "Frames must not be negative.");
        if (Running)
            Volatile.Write(ref _beats, _beats + frames / (double)SampleRate * Bpm / 60.0);
        Volatile.Write(ref _samplePosition, _samplePosition + frames);
    }

    /// <summary>Jumps to another place, as a transport locate does.</summary>
    /// <param name="samplePosition">The frame number the next block starts at.</param>
    /// <param name="beats">The musical position, in beats, of that frame.</param>
    public void Seek(long samplePosition, double beats)
    {
        if (samplePosition < 0)
            throw new ArgumentOutOfRangeException(nameof(samplePosition), samplePosition, "Position must not be negative.");
        if (!double.IsFinite(beats))
            throw new ArgumentOutOfRangeException(nameof(beats), beats, "Beats must be a finite number.");
        Volatile.Write(ref _beats, beats);
        Volatile.Write(ref _samplePosition, samplePosition);
    }

    private static void ValidateTempo(double bpm)
    {
        if (!double.IsFinite(bpm) || bpm <= 0)
            throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "The tempo must be positive.");
    }
}
