using Sigil.Sound.Operators;

namespace Sigil.Sound;

/// <summary>
/// A hot-swappable place in a chain. <see cref="Sound"/> can go anywhere a sound goes;
/// <see cref="Swap"/> replaces what plays there in every running render, with a short
/// equal-power crossfade so the change doesn't click.
/// </summary>
/// <remarks>
/// <see cref="Swap"/> compiles the new sound on the calling thread, never on the audio thread,
/// and hands it over with an atomic exchange that the audio thread picks up on its next block.
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

    private readonly object _gate = new();
    private readonly List<WeakReference<SlotOperator>> _instances = [];
    private Sound _current;

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
    /// Replaces the slot's sound in every running render, crossfading over <paramref name="fadeSeconds"/>
    /// (default <see cref="FadeSeconds"/>; zero swaps on the next block without a fade).
    /// Compiles on the calling thread; safe to call while another thread renders.
    /// </summary>
    public void Swap(Sound next, double? fadeSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(next);
        double fade = fadeSeconds ?? FadeSeconds;
        ValidateFade(fade);

        lock (_gate)
        {
            _current = next;
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (!_instances[i].TryGetTarget(out var instance))
                {
                    _instances.RemoveAt(i);
                    continue;
                }

                instance.DisposeRetired();
                var op = next.Compile(instance.Context);
                instance.Post(op, (int)Math.Round(fade * instance.Context.SampleRate));
            }

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
        lock (_gate)
        {
            // Each slot instance is its own compile root, so retiring it disposes exactly what it built.
            var own = context with { Scope = null };
            var instance = new SlotOperator(_current.Compile(own), own);
            _instances.Add(new WeakReference<SlotOperator>(instance));
            return instance;
        }
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
