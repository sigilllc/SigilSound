using System.Runtime.CompilerServices;

namespace Sigil.Sound;

/// <summary>
/// Values arranged in cycle time, as a question you can ask of any stretch of time: "what happens between 3 and 4.5?".
/// Because it is a function and not a stored list, <c>Fast</c>, <c>Rev</c>, <c>Every</c> and friends compose freely and a
/// pattern can run forever or change from cycle to cycle. Build one with <see cref="Pat"/> or <see cref="Mini"/>
/// (<c>Mini.Notes("c3 [e3 g3]*2 ~ &lt;b3 d4&gt;")</c>), reshape it with the methods on <see cref="PatternExtensions"/>.
/// </summary>
/// <remarks>
/// Time is in cycles, not beats or seconds: cycle 0 is [0, 1), cycle 1 is [1, 2). How long a cycle lasts is up to whatever plays
/// the pattern (<see cref="Pat.BeatsPerCycle"/> beats when converted with <c>ToPattern</c>).
/// Queries fill a list you own, so asking again with the same list allocates nothing once it has grown.
/// </remarks>
/// <typeparam name="T">What happens at each event: a <see cref="Note"/>, a number to drive a parameter, anything.</typeparam>
public sealed class Pattern<T>
{
    private readonly Action<Arc, List<PatternEvent<T>>> _query;

    internal Pattern(Action<Arc, List<PatternEvent<T>>> query) => _query = query;

    /// <summary>
    /// Adds the events that touch <paramref name="arc"/> to <paramref name="output"/>, ordered by when they start.
    /// The list is not cleared first. An event that straddles the end of the arc comes back cut to the arc (see
    /// <see cref="PatternEvent{T}.Part"/>); check <see cref="PatternEvent{T}.HasOnset"/> to play only those that start in it.
    /// </summary>
    public void Query(Arc arc, List<PatternEvent<T>> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (arc.End < arc.Begin) throw new ArgumentException("The arc ends before it begins.", nameof(arc));

        int start = output.Count;
        _query(arc, output);
        SortFrom(output, start);
    }

    /// <summary>The events of one cycle, in a new list. Convenient for tests and tools; real-time code should call <see cref="Query"/>.</summary>
    public List<PatternEvent<T>> Cycle(long cycle)
    {
        var events = new List<PatternEvent<T>>();
        Query(new Arc(new Fraction(cycle), new Fraction(cycle + 1)), events);
        return events;
    }

    // Combinators call each other through this, skipping the sort that only the outermost query needs.
    internal void QueryInto(Arc arc, List<PatternEvent<T>> output)
    {
        // Every nested combinator passes through here, so this one check stops a pattern built too deeply (thousands of
        // chained calls) before it can overflow the stack; it throws a catchable exception instead.
        RuntimeHelpers.EnsureSufficientExecutionStack();
        _query(arc, output);
    }

    /// <summary>
    /// Asks the pattern for its first <paramref name="cycles"/> cycles and throws if that fails, so a pattern that cannot be played
    /// (too many events a cycle, or nested too deeply) is refused when it is built and not found out while audio is running.
    /// <see cref="Pat.MaxEvents"/> is the limit. Playing a pattern checks it for you; this is for code that consumes one itself.
    /// </summary>
    /// <param name="cycles">How many cycles to check, counting from cycle 0.</param>
    /// <exception cref="InvalidOperationException">The pattern is too dense or too deeply nested.</exception>
    public void Validate(int cycles = 64)
    {
        if (cycles < 1) throw new ArgumentOutOfRangeException(nameof(cycles), cycles, "Check at least one cycle.");
        var scratch = new List<PatternEvent<T>>();
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            scratch.Clear();
            try
            {
                Query(new Arc(new Fraction(cycle), new Fraction(cycle + 1)), scratch);
            }
            catch (InsufficientExecutionStackException)
            {
                throw new InvalidOperationException("The pattern is nested too deeply to play; build it with fewer chained operations.");
            }
            catch (OverflowException)
            {
                throw new InvalidOperationException("The pattern's speed factors multiply out to a time too large to represent; use smaller Fast, Slow and Shift values.");
            }
        }
    }

    // For the audio thread: a pattern that fails (it was valid when built, but a later cycle is too dense) must not throw
    // out of the render. The partial result is dropped and the caller treats the block as empty.
    internal bool TryQuery(Arc arc, List<PatternEvent<T>> output)
    {
        int start = output.Count;
        try
        {
            Query(arc, output);
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or InsufficientExecutionStackException or OverflowException)
        {
            output.RemoveRange(start, output.Count - start);
            return false;
        }
    }

    // A stable insertion sort of the new tail: lists are short (a cycle's worth) and this allocates nothing.
    private static void SortFrom(List<PatternEvent<T>> list, int start)
    {
        for (int i = start + 1; i < list.Count; i++)
        {
            var item = list[i];
            int j = i - 1;
            while (j >= start && Order(list[j], item) > 0)
            {
                list[j + 1] = list[j];
                j--;
            }
            list[j + 1] = item;
        }
    }

    private static int Order(in PatternEvent<T> a, in PatternEvent<T> b)
    {
        int byPart = a.Part.Begin.CompareTo(b.Part.Begin);
        return byPart != 0 ? byPart : a.Whole.Begin.CompareTo(b.Whole.Begin);
    }
}

/// <summary>Building patterns from scratch: <c>using static Sigil.Sound.Pat;</c> then <c>Seq(Pure(1), Pure(2))</c>.</summary>
public static class Pat
{
    /// <summary>How many beats one cycle lasts when a pattern becomes a <see cref="Pattern"/>: one bar of 4/4.</summary>
    public const double BeatsPerCycle = 4;

    /// <summary>The most events one query may collect before it is stopped as runaway.</summary>
    public const int MaxEvents = 100_000;

    /// <summary>Nothing, forever.</summary>
    public static Pattern<T> Silence<T>() => new((_, _) => { });

    /// <summary>One event per cycle, lasting the whole cycle.</summary>
    public static Pattern<T> Pure<T>(T value) => new((arc, output) =>
    {
        foreach (var piece in arc.SplitCycles())
        {
            // Every event starts at a Pure, so this is the one place that stops a runaway Fast from filling memory.
            if (output.Count >= MaxEvents)
                throw new InvalidOperationException($"The query produced more than {MaxEvents:N0} events; a pattern is far too dense (check Fast, *, ! and Euclid).");
            long cycle = piece.Begin.Floor();
            output.Add(new PatternEvent<T>(new Arc(new Fraction(cycle), new Fraction(cycle + 1)), piece, value));
        }
    });

    /// <summary>All the patterns at once, layered on top of each other (chords, drums over a bass line).</summary>
    public static Pattern<T> Stack<T>(params Pattern<T>[] patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        var layers = (Pattern<T>[])patterns.Clone();
        return new((arc, output) =>
        {
            foreach (var layer in layers)
                layer.QueryInto(arc, output);
        });
    }

    /// <summary>
    /// One pattern per cycle, in turn: cycle 0 plays the first, cycle 1 the second, and so on, then around again.
    /// Each pattern carries on from where it left off, so <c>Cat(a, b)</c> plays a's cycle 0, b's cycle 0, a's cycle 1, ...
    /// </summary>
    public static Pattern<T> Cat<T>(params Pattern<T>[] patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        if (patterns.Length == 0) return Silence<T>();
        var turns = (Pattern<T>[])patterns.Clone();
        long count = turns.Length;
        return new((arc, output) =>
        {
            foreach (var piece in arc.SplitCycles())
            {
                long cycle = piece.Begin.Floor();
                long index = ((cycle % count) + count) % count;
                // The chosen pattern has only lived through the cycles it was chosen for, so shift its clock to match.
                var offset = new Fraction(cycle - FloorDiv(cycle, count));
                int start = output.Count;
                turns[index].QueryInto(new Arc(piece.Begin - offset, piece.End - offset), output);
                Shift(output, start, offset);
            }
        });
    }

    /// <summary>The patterns one after another within every cycle, each taking an equal share of it: <c>Seq(a, b, c)</c> is "a b c".</summary>
    public static Pattern<T> Seq<T>(params Pattern<T>[] patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        var steps = new (Pattern<T>, Fraction)[patterns.Length];
        for (int i = 0; i < steps.Length; i++)
            steps[i] = (patterns[i], Fraction.One);
        return Weighted(steps);
    }

    // Steps share the cycle in proportion to their weights; each is squeezed into its own window.
    internal static Pattern<T> Weighted<T>(IReadOnlyList<(Pattern<T> Pattern, Fraction Weight)> steps)
    {
        if (steps.Count == 0) return Silence<T>();
        var total = Fraction.Zero;
        foreach (var step in steps) total += step.Weight;

        var layers = new Pattern<T>[steps.Count];
        var begin = Fraction.Zero;
        for (int i = 0; i < layers.Length; i++)
        {
            var end = begin + steps[i].Weight / total;
            layers[i] = Compress(steps[i].Pattern, begin, end);
            begin = end;
        }

        return layers.Length == 1 ? layers[0] : Stack(layers);
    }

    // Plays each cycle of the pattern squeezed into [begin, end) of the outer cycle, and nothing outside it.
    internal static Pattern<T> Compress<T>(Pattern<T> pattern, Fraction begin, Fraction end)
    {
        var width = end - begin;
        return new((arc, output) =>
        {
            foreach (var piece in arc.SplitCycles())
            {
                var cycle = new Fraction(piece.Begin.Floor());
                var windowBegin = cycle + begin;
                if (!piece.TryIntersect(new Arc(windowBegin, cycle + end), out var inside)) continue;

                var inner = new Arc(cycle + (inside.Begin - windowBegin) / width, cycle + (inside.End - windowBegin) / width);
                int start = output.Count;
                pattern.QueryInto(inner, output);
                for (int i = start; i < output.Count; i++)
                    output[i] = output[i].Mapped(width, windowBegin - cycle * width);
            }
        });
    }

    internal static void Shift<T>(List<PatternEvent<T>> events, int start, Fraction offset)
    {
        if (offset == Fraction.Zero) return;
        for (int i = start; i < events.Count; i++)
            events[i] = events[i].Mapped(Fraction.One, offset);
    }

    internal static long FloorDiv(long a, long b)
    {
        long q = a / b;
        return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q;
    }
}

/// <summary>Reshaping patterns. Every method returns a new pattern and leaves the original alone.</summary>
public static class PatternExtensions
{
    /// <summary>Plays the pattern <paramref name="factor"/> times as fast: <c>Fast(2)</c> fits two of its cycles into one.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The factor is not positive.</exception>
    public static Pattern<T> Fast<T>(this Pattern<T> pattern, Fraction factor)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (factor <= Fraction.Zero) throw new ArgumentOutOfRangeException(nameof(factor), factor, "The factor must be positive.");
        var inverse = factor.Reciprocal();
        return new((arc, output) =>
        {
            int start = output.Count;
            pattern.QueryInto(new Arc(arc.Begin * factor, arc.End * factor), output);
            for (int i = start; i < output.Count; i++)
                output[i] = output[i].Mapped(inverse, Fraction.Zero);
        });
    }

    /// <inheritdoc cref="Fast{T}(Pattern{T}, Fraction)"/>
    public static Pattern<T> Fast<T>(this Pattern<T> pattern, int factor) => pattern.Fast(new Fraction(factor));

    /// <inheritdoc cref="Fast{T}(Pattern{T}, Fraction)"/>
    public static Pattern<T> Fast<T>(this Pattern<T> pattern, double factor) => pattern.Fast(Fraction.FromDouble(factor));

    /// <summary>Plays the pattern <paramref name="factor"/> times as slow: <c>Slow(2)</c> stretches one cycle over two.</summary>
    public static Pattern<T> Slow<T>(this Pattern<T> pattern, Fraction factor)
    {
        if (factor <= Fraction.Zero) throw new ArgumentOutOfRangeException(nameof(factor), factor, "The factor must be positive.");
        return pattern.Fast(factor.Reciprocal());
    }

    /// <inheritdoc cref="Slow{T}(Pattern{T}, Fraction)"/>
    public static Pattern<T> Slow<T>(this Pattern<T> pattern, int factor) => pattern.Slow(new Fraction(factor));

    /// <inheritdoc cref="Slow{T}(Pattern{T}, Fraction)"/>
    public static Pattern<T> Slow<T>(this Pattern<T> pattern, double factor) => pattern.Slow(Fraction.FromDouble(factor));

    /// <summary>Moves the whole pattern later by <paramref name="cycles"/> (earlier if negative).</summary>
    public static Pattern<T> Shift<T>(this Pattern<T> pattern, Fraction cycles)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new((arc, output) =>
        {
            int start = output.Count;
            pattern.QueryInto(new Arc(arc.Begin - cycles, arc.End - cycles), output);
            Pat.Shift(output, start, cycles);
        });
    }

    /// <inheritdoc cref="Shift{T}(Pattern{T}, Fraction)"/>
    public static Pattern<T> Shift<T>(this Pattern<T> pattern, double cycles) => pattern.Shift(Fraction.FromDouble(cycles));

    /// <summary>Plays each cycle backwards.</summary>
    public static Pattern<T> Rev<T>(this Pattern<T> pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new((arc, output) =>
        {
            foreach (var piece in arc.SplitCycles())
            {
                // Mirror around the middle of the cycle: t becomes (2c + 1) - t.
                var sum = new Fraction(2 * piece.Begin.Floor() + 1);
                int start = output.Count;
                pattern.QueryInto(new Arc(sum - piece.End, sum - piece.Begin), output);
                for (int i = start; i < output.Count; i++)
                {
                    var e = output[i];
                    output[i] = new PatternEvent<T>(new Arc(sum - e.Whole.End, sum - e.Whole.Begin), new Arc(sum - e.Part.End, sum - e.Part.Begin), e.Value);
                }
            }
        });
    }

    /// <summary>Applies <paramref name="transform"/> to the pattern on every <paramref name="cycles"/>th cycle (0, n, 2n, ...) and leaves the rest alone.</summary>
    /// <example>
    /// <code>
    /// // Every fourth cycle the pattern runs twice as fast and backwards.
    /// Mini.Notes("c3 e3 g3 b3").Every(4, p =&gt; p.Fast(2).Rev())
    ///     .Play(v =&gt; Saw(v.Frequency).LowPass(2000).Adsr(0.005, 0.1, 0.4, 0.2), bpm: 120) * 0.3
    /// </code>
    /// </example>
    public static Pattern<T> Every<T>(this Pattern<T> pattern, int cycles, Func<Pattern<T>, Pattern<T>> transform)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(transform);
        if (cycles < 1) throw new ArgumentOutOfRangeException(nameof(cycles), cycles, "Every needs at least 1 cycle.");
        var changed = transform(pattern) ?? throw new InvalidOperationException("The transform returned null.");
        return new((arc, output) =>
        {
            foreach (var piece in arc.SplitCycles())
            {
                bool apply = ((piece.Begin.Floor() % cycles) + cycles) % cycles == 0;
                (apply ? changed : pattern).QueryInto(piece, output);
            }
        });
    }

    /// <summary>
    /// Drops events at random, the same ones every time you ask (the choice comes from where the event sits in time and
    /// <paramref name="seed"/>, not from a clock), so a loop is stable and a hot swap or offline render matches live playback.
    /// </summary>
    /// <param name="pattern">The pattern to thin out.</param>
    /// <param name="amount">The share to drop, from 0 (none) to 1 (all). Defaults to half.</param>
    /// <param name="seed">Different seeds drop different events.</param>
    public static Pattern<T> Degrade<T>(this Pattern<T> pattern, double amount = 0.5, int seed = 0) =>
        Filter(pattern, amount, seed, keepAbove: true);

    /// <summary>Applies <paramref name="transform"/> to a random <paramref name="amount"/> of the events and leaves the others as they are.</summary>
    public static Pattern<T> Sometimes<T>(this Pattern<T> pattern, double amount, Func<Pattern<T>, Pattern<T>> transform, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var chosen = Filter(pattern, amount, seed, keepAbove: false);
        return Pat.Stack(Filter(pattern, amount, seed, keepAbove: true), transform(chosen));
    }

    private static Pattern<T> Filter<T>(Pattern<T> pattern, double amount, int seed, bool keepAbove)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (!(amount is >= 0 and <= 1)) throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount must be between 0 and 1.");
        return new((arc, output) =>
        {
            int start = output.Count;
            pattern.QueryInto(arc, output);
            int write = start;
            for (int read = start; read < output.Count; read++)
            {
                bool above = PatternRandom.Value(seed, output[read].Whole.Begin) >= amount;
                if (above == keepAbove)
                    output[write++] = output[read];
            }
            output.RemoveRange(write, output.Count - write);
        });
    }

    /// <summary>
    /// Spreads <paramref name="pulses"/> hits as evenly as possible over <paramref name="steps"/> steps (a Euclidean rhythm): (3,8) is x..x..x.
    /// The rhythm decides when things happen and each hit takes whatever the pattern is playing at that moment, so
    /// <c>"[a b]"</c> with (3,8) gives a, a, b, as in Strudel.
    /// </summary>
    /// <param name="pattern">Where the values come from; sampled at the start of each hit.</param>
    /// <param name="pulses">How many steps sound, from 0 to <paramref name="steps"/>.</param>
    /// <param name="steps">How many steps the cycle is divided into.</param>
    /// <param name="rotation">Steps to rotate the rhythm left.</param>
    public static Pattern<T> Euclid<T>(this Pattern<T> pattern, int pulses, int steps, int rotation = 0)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var hits = Euclidean.Rhythm(pulses, steps, rotation);
        var cache = new List<PatternEvent<T>>();
        return new((arc, output) =>
        {
            foreach (var piece in arc.SplitCycles())
            {
                var cycle = new Fraction(piece.Begin.Floor());
                for (int i = 0; i < hits.Length; i++)
                {
                    if (!hits[i]) continue;
                    var stepBegin = cycle + new Fraction(i, steps);
                    var step = new Arc(stepBegin, cycle + new Fraction(i + 1, steps));
                    if (!piece.TryIntersect(step, out var part)) continue;

                    // The value is whatever the pattern is playing at the instant the step starts. Scratch list as in Select.
                    var buffer = Interlocked.Exchange(ref cache, null!) ?? new List<PatternEvent<T>>();
                    pattern.QueryInto(new Arc(stepBegin, stepBegin), buffer);
                    foreach (var e in buffer)
                        output.Add(new PatternEvent<T>(step, part, e.Value));
                    buffer.Clear();
                    cache = buffer;
                }
            }
        });
    }

    /// <summary>Changes every value.</summary>
    public static Pattern<TResult> Select<T, TResult>(this Pattern<T> pattern, Func<T, TResult> map)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(map);
        var cache = new List<PatternEvent<T>>();
        return new((arc, output) =>
        {
            // The values change type, so the source events need a scratch list. Each mapped pattern keeps one for reuse; a
            // second query that overlaps the first (another thread, or the pattern nested in itself) takes a fresh list instead.
            var buffer = Interlocked.Exchange(ref cache, null!) ?? new List<PatternEvent<T>>();
            pattern.QueryInto(arc, buffer);
            foreach (var e in buffer)
                output.Add(new PatternEvent<TResult>(e.Whole, e.Part, map(e.Value)));
            buffer.Clear();
            cache = buffer;
        });
    }

    /// <summary>Turns each event into several at the same time, one per item the function returns (a chord into its notes).</summary>
    public static Pattern<TResult> SelectMany<T, TResult>(this Pattern<T> pattern, Func<T, IReadOnlyList<TResult>> expand)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(expand);
        var cache = new List<PatternEvent<T>>();
        return new((arc, output) =>
        {
            var buffer = Interlocked.Exchange(ref cache, null!) ?? new List<PatternEvent<T>>();
            pattern.QueryInto(arc, buffer);
            foreach (var e in buffer)
            {
                var items = expand(e.Value);
                for (int i = 0; i < items.Count; i++)
                    output.Add(new PatternEvent<TResult>(e.Whole, e.Part, items[i]));
            }
            buffer.Clear();
            cache = buffer;
        });
    }

    /// <summary>Keeps only the events whose value passes <paramref name="keep"/>.</summary>
    public static Pattern<T> Where<T>(this Pattern<T> pattern, Func<T, bool> keep)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(keep);
        return new((arc, output) =>
        {
            int start = output.Count;
            pattern.QueryInto(arc, output);
            int write = start;
            for (int read = start; read < output.Count; read++)
                if (keep(output[read].Value))
                    output[write++] = output[read];
            output.RemoveRange(write, output.Count - write);
        });
    }
}

// A deterministic number in [0, 1) from a seed and a moment in time.
internal static class PatternRandom
{
    public static double Value(int seed, Fraction time)
    {
        ulong x = (ulong)seed * 0x9E3779B97F4A7C15UL;
        x ^= (ulong)time.Numerator * 0xBF58476D1CE4E5B9UL;
        x ^= (ulong)time.Denominator * 0x94D049BB133111EBUL;
        x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27; x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (x >> 11) * (1.0 / (1UL << 53));
    }
}

// Bjorklund's algorithm, as Tidal writes it: repeatedly pair up the longer group with the shorter until one is left.
internal static class Euclidean
{
    public const int MaxSteps = 1024;

    public static bool[] Rhythm(int pulses, int steps, int rotation)
    {
        if (steps < 1 || steps > MaxSteps) throw new ArgumentOutOfRangeException(nameof(steps), steps, $"A rhythm needs between 1 and {MaxSteps} steps.");
        if (pulses < 0 || pulses > steps) throw new ArgumentOutOfRangeException(nameof(pulses), pulses, "Pulses must be between 0 and the number of steps.");

        var xs = new List<List<bool>>();
        var ys = new List<List<bool>>();
        for (int i = 0; i < pulses; i++) xs.Add([true]);
        for (int i = 0; i < steps - pulses; i++) ys.Add([false]);
        int i0 = pulses, j0 = steps - pulses;

        while (Math.Min(i0, j0) > 1)
        {
            if (i0 > j0)
            {
                // Pair each of the j shorter groups with a longer one; the leftover longer groups become the new remainder.
                var head = xs.GetRange(0, j0);
                var rest = xs.GetRange(j0, xs.Count - j0);
                for (int k = 0; k < j0; k++) head[k] = [.. head[k], .. ys[k]];
                (xs, ys) = (head, rest);
                (i0, j0) = (j0, i0 - j0);
            }
            else
            {
                var tail = ys.GetRange(0, i0);
                var rest = ys.GetRange(i0, ys.Count - i0);
                for (int k = 0; k < i0; k++) xs[k] = [.. xs[k], .. tail[k]];
                ys = rest;
                j0 -= i0;
            }
        }

        var flat = xs.Concat(ys).SelectMany(g => g).ToArray();
        var rotated = new bool[steps];
        for (int i = 0; i < steps; i++)
            rotated[i] = flat[(((i + rotation) % steps) + steps) % steps];
        return rotated;
    }
}
