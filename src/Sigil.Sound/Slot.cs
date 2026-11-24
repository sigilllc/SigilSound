using Sigil.Sound.Operators;

namespace Sigil.Sound;

/// <summary>
/// A hot-swappable place in a chain. <see cref="Sound"/> can go anywhere a sound goes;
/// <see cref="Swap"/> replaces what plays there in every running render, with a short
/// crossfade so the change doesn't click.
/// </summary>
/// <remarks>
/// <see cref="Swap"/> compiles the new sound on the calling thread, never on the audio thread,
/// and hands it over with an atomic exchange that the audio thread picks up on its next block.
/// A swap made while an earlier one is still fading starts a new fade from where the sounds have got to,
/// so swaps can follow each other as fast as you like without a click.
/// Replaced operators that implement <see cref="IDisposable"/> are disposed on a later
/// <see cref="Swap"/> call, once the audio thread has let go of them.
/// </remarks>
/// <example>
/// <code>
/// // A slot is a place in a graph whose sound can be replaced while it plays, without a click.
/// var slot = new Slot(Sine(220));
/// var chain = slot.Sound.LowPass(800) * 0.5;
/// slot.Swap(Saw(220));   // from any thread, at any time: the new sound crossfades in
/// chain
/// </code>
/// </example>
public sealed class Slot
{
    /// <summary>The default crossfade length: 20 ms.</summary>
    public const double DefaultFadeSeconds = 0.02;

    // _gate guards the fields below and is only ever held briefly; _swapGate orders the swaps and is held while one compiles.
    private readonly object _gate = new();
    private readonly object _swapGate = new();
    private readonly List<WeakReference<SlotOperator>> _instances = [];
    private Sound _current;

    // Counts the swaps, so a running render that hears about two of them out of order plays the later one.
    private long _version;

    // One timer per slot, re-armed by each swap, instead of a task and closure per swap per running render.
    private Timer? _cleanup;

    /// <summary>Creates a slot that plays <paramref name="initial"/> until it is swapped.</summary>
    /// <param name="initial">The sound to play first.</param>
    /// <param name="fadeSeconds">How long a swap crossfades from the old sound to the new one.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fadeSeconds"/> is negative or not a number.</exception>
    public Slot(Sound initial, double fadeSeconds = DefaultFadeSeconds)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ValidateFade(fadeSeconds);
        _current = initial;
        FadeSeconds = fadeSeconds;
        Sound = Sound.FromOperator(Attach, "Slot");
    }

    /// <summary>The sound to place in a chain. Every compile of it follows this slot's swaps.</summary>
    public Sound Sound { get; }

    /// <summary>What the slot plays now: the initial sound or the last one swapped in.</summary>
    public Sound Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>The crossfade length <see cref="Swap"/> uses when none is given. Zero swaps instantly.</summary>
    public double FadeSeconds { get; }

    /// <summary>
    /// The shape of the crossfade the next swaps make: <see cref="FadeCurve.EqualPower"/> (the default) for audio,
    /// <see cref="FadeCurve.Linear"/> for a slot that carries a control signal, so its level does not swell during a swap.
    /// </summary>
    /// <example>
    /// <code>
    /// // A slot that holds a modulation signal: a linear fade keeps its level steady while it is swapped.
    /// var sweep = new Slot(Sine(0.5).Range(400, 2000)) { FadeCurve = FadeCurve.Linear };
    /// Saw(110).LowPass(sweep.Sound) * 0.4
    /// </code>
    /// </example>
    public FadeCurve FadeCurve { get; set; }

    /// <summary>
    /// Replaces the slot's sound in every running render, crossfading over <paramref name="fadeSeconds"/>
    /// (default <see cref="FadeSeconds"/>; zero swaps on the next block without a fade).
    /// Compiles on the calling thread; safe to call while another thread renders.
    /// </summary>
    public void Swap(Sound next, double? fadeSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(next);
        double fade = fadeSeconds ?? FadeSeconds;
        ValidateFade(fade);

        // One swap at a time, so two swaps from two threads land in the order they took their turn. This lock is held while
        // compiling; the gate below never is, so asking the slot what it plays does not wait for a long compile.
        var curve = FadeCurve;
        lock (_swapGate)
        {
            List<SlotOperator> running = [];
            long version;
            lock (_gate)
            {
                // A sound attached from here on compiles the new sound itself, and one attached before is in the list.
                _current = next;
                version = ++_version;
                for (int i = _instances.Count - 1; i >= 0; i--)
                {
                    if (_instances[i].TryGetTarget(out var instance))
                        running.Add(instance);
                    else
                        _instances.RemoveAt(i);
                }
            }

            foreach (var instance in running)
            {
                instance.DisposeRetired();
                var op = next.Compile(instance.Context);
                instance.Post(op, (int)Math.Round(fade * instance.Context.SampleRate), curve, version);
            }

            lock (_gate)
                ScheduleCleanup(fade);
        }
    }

    /// <summary>Uses the slot as a sound, to place it in a graph.</summary>
    public static implicit operator Sound(Slot slot) => slot.Sound;

    /// <summary>Uses the slot as a parameter, so a swappable sound can modulate another.</summary>
    public static implicit operator Param(Slot slot) => slot.Sound;

    /// <summary>A short description of the slot and the sound it holds now.</summary>
    public override string ToString() => $"Slot({Current})";

    private SlotOperator Attach(RenderContext context)
    {
        // Each slot instance is its own compile root, so retiring it disposes exactly what it built.
        var own = context with { Scope = null };

        // Compile outside the gate, so asking the slot what it plays never waits for this compile.
        Sound sound;
        long version;
        lock (_gate)
        {
            sound = _current;
            version = _version;
        }

        var instance = new SlotOperator(sound.Compile(own), version, own);

        // A swap made during the compile did not know about this instance, so catch up with it here. A swap after this point
        // finds the instance in the list, and the versions keep the later sound if the two arrive out of order.
        Sound latest;
        long latestVersion;
        lock (_gate)
        {
            _instances.Add(new WeakReference<SlotOperator>(instance));
            latest = _current;
            latestVersion = _version;
        }

        if (latestVersion != version)
            instance.Post(latest.Compile(own), (int)Math.Round(FadeSeconds * own.SampleRate), FadeCurve, latestVersion);
        return instance;
    }

    // Once the crossfade is over the audio thread has retired the old sound; dispose it then, rather than
    // leaving it until the next swap. If nothing is rendering, the next swap or disposal still collects it.
    // Called under the gate. A later swap supersedes an earlier fade, so re-arming for the latest one is enough.
    private void ScheduleCleanup(double fadeSeconds)
    {
        var delay = TimeSpan.FromSeconds(fadeSeconds + 0.25);
        if (_cleanup is null)
            _cleanup = new Timer(static state => ((Slot)state!).DisposeRetired(), this, delay, Timeout.InfiniteTimeSpan);
        else
            _cleanup.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void DisposeRetired()
    {
        lock (_gate)
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (_instances[i].TryGetTarget(out var instance))
                    instance.DisposeRetired();
                else
                    _instances.RemoveAt(i);
            }
        }
    }

    private static void ValidateFade(double fadeSeconds)
    {
        if (fadeSeconds < 0 || double.IsNaN(fadeSeconds))
            throw new ArgumentOutOfRangeException(nameof(fadeSeconds), fadeSeconds, "Fade must not be negative.");
    }
}
