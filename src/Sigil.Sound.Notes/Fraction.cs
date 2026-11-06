using System.Globalization;

namespace Sigil.Sound;

/// <summary>
/// An exact fraction of a cycle. Pattern time is rational so that <c>Fast(3)</c> followed by <c>Slow(3)</c> lands exactly
/// where it started and a loop does not drift after hours, as floating-point beats would.
/// </summary>
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
    private readonly long _numerator;
    private readonly long _denominator; // zero only in default(Fraction), which reads as 0/1

    /// <summary>Creates <paramref name="numerator"/> / <paramref name="denominator"/> in lowest terms.</summary>
    /// <exception cref="DivideByZeroException">The denominator is zero.</exception>
    public Fraction(long numerator, long denominator = 1)
    {
        if (denominator == 0) throw new DivideByZeroException("A fraction needs a non-zero denominator.");
        if (denominator < 0)
        {
            numerator = checked(-numerator);
            denominator = checked(-denominator);
        }

        long divisor = Gcd(Math.Abs(numerator), denominator);
        _numerator = numerator / divisor;
        _denominator = denominator / divisor;
    }

    private Fraction(long numerator, long denominator, bool _)
    {
        _numerator = numerator;
        _denominator = denominator;
    }

    /// <summary>The numerator, in lowest terms.</summary>
    public long Numerator => _numerator;

    /// <summary>The denominator, in lowest terms; always positive.</summary>
    public long Denominator => _denominator == 0 ? 1 : _denominator;

    /// <summary>Zero.</summary>
    public static Fraction Zero => default;

    /// <summary>One.</summary>
    public static Fraction One => new(1, 1, false);

    /// <summary>The nearest simple fraction to <paramref name="value"/> (denominator up to 100000), by continued fractions.</summary>
    public static Fraction FromDouble(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1e12)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a finite number of moderate size.");

        long whole = (long)Math.Floor(value);
        double rest = value - whole;
        long h0 = 0, h1 = 1, k0 = 1, k1 = 0; // convergents of the fractional part
        double x = rest;
        for (int i = 0; i < 24; i++)
        {
            long a = (long)Math.Floor(x);
            long h2 = a * h1 + h0, k2 = a * k1 + k0;
            if (k2 > 100_000) break;
            (h0, h1, k0, k1) = (h1, h2, k1, k2);
            double frac = x - a;
            if (frac < 1e-9 || Math.Abs((double)h1 / k1 - rest) < 1e-9) break;
            x = 1.0 / frac;
        }

        return k1 == 0 ? new Fraction(whole) : new Fraction(whole * k1 + h1, k1);
    }

    /// <summary>A whole number as a fraction.</summary>
    public static implicit operator Fraction(int value) => new(value);

    /// <summary>A whole number as a fraction.</summary>
    public static implicit operator Fraction(long value) => new(value);

    /// <summary>The nearest simple fraction; see <see cref="FromDouble"/>.</summary>
    public static explicit operator Fraction(double value) => FromDouble(value);

    /// <summary>The fraction as a floating-point number.</summary>
    public static explicit operator double(Fraction value) => value.ToDouble();

    /// <summary>The fraction as a floating-point number.</summary>
    public double ToDouble() => (double)_numerator / Denominator;

    /// <summary>The largest whole number not above this fraction.</summary>
    public long Floor()
    {
        long d = Denominator;
        long q = _numerator / d;
        return _numerator % d < 0 ? q - 1 : q;
    }

    /// <summary>One divided by this fraction.</summary>
    /// <exception cref="DivideByZeroException">The fraction is zero.</exception>
    public Fraction Reciprocal() => _numerator == 0
        ? throw new DivideByZeroException("Zero has no reciprocal.")
        : new Fraction(Denominator, _numerator);

    /// <summary>The exact sum.</summary>
    /// <exception cref="OverflowException">The result does not fit.</exception>
    public static Fraction operator +(Fraction a, Fraction b) =>
        new(checked(a._numerator * b.Denominator + b._numerator * a.Denominator), checked(a.Denominator * b.Denominator));

    /// <summary>The exact difference.</summary>
    /// <exception cref="OverflowException">The result does not fit.</exception>
    public static Fraction operator -(Fraction a, Fraction b) =>
        new(checked(a._numerator * b.Denominator - b._numerator * a.Denominator), checked(a.Denominator * b.Denominator));

    /// <summary>The negated fraction.</summary>
    public static Fraction operator -(Fraction a) => new(-a._numerator, a.Denominator, false);

    /// <summary>The exact product.</summary>
    /// <exception cref="OverflowException">The result does not fit.</exception>
    public static Fraction operator *(Fraction a, Fraction b) =>
        new(checked(a._numerator * b._numerator), checked(a.Denominator * b.Denominator));

    /// <summary>The exact quotient.</summary>
    /// <exception cref="DivideByZeroException">The divisor is zero.</exception>
    /// <exception cref="OverflowException">The result does not fit.</exception>
    public static Fraction operator /(Fraction a, Fraction b) =>
        b._numerator == 0 ? throw new DivideByZeroException() : new(checked(a._numerator * b.Denominator), checked(a.Denominator * b._numerator));

    /// <summary>True when both are the same value, in any form (2/4 equals 1/2).</summary>
    public static bool operator ==(Fraction a, Fraction b) => a._numerator == b._numerator && a.Denominator == b.Denominator;

    /// <summary>True when the values differ.</summary>
    public static bool operator !=(Fraction a, Fraction b) => !(a == b);

    /// <summary>True when <paramref name="a"/> is less than <paramref name="b"/>.</summary>
    public static bool operator <(Fraction a, Fraction b) => a.CompareTo(b) < 0;

    /// <summary>True when <paramref name="a"/> is greater than <paramref name="b"/>.</summary>
    public static bool operator >(Fraction a, Fraction b) => a.CompareTo(b) > 0;

    /// <summary>True when <paramref name="a"/> is less than or equal to <paramref name="b"/>.</summary>
    public static bool operator <=(Fraction a, Fraction b) => a.CompareTo(b) <= 0;

    /// <summary>True when <paramref name="a"/> is greater than or equal to <paramref name="b"/>.</summary>
    public static bool operator >=(Fraction a, Fraction b) => a.CompareTo(b) >= 0;

    /// <summary>The smaller of two fractions.</summary>
    public static Fraction Min(Fraction a, Fraction b) => a <= b ? a : b;

    /// <summary>The larger of two fractions.</summary>
    public static Fraction Max(Fraction a, Fraction b) => a >= b ? a : b;

    // Cross-multiplied in 128 bits so comparing two fractions can never overflow.
    /// <inheritdoc />
    public int CompareTo(Fraction other) =>
        ((Int128)_numerator * other.Denominator).CompareTo((Int128)other._numerator * Denominator);

    /// <inheritdoc />
    public bool Equals(Fraction other) => this == other;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Fraction other && this == other;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_numerator, Denominator);

    /// <summary>The fraction in lowest terms: <c>3</c> for a whole number, <c>3/4</c> otherwise.</summary>
    public override string ToString() =>
        Denominator == 1
            ? _numerator.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{_numerator}/{Denominator}");

    private static long Gcd(long a, long b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a == 0 ? 1 : a;
    }
}

/// <summary>A stretch of cycle time, from <see cref="Begin"/> up to but not including <see cref="End"/>.</summary>
public readonly record struct Arc(Fraction Begin, Fraction End)
{
    /// <summary>The length of the arc, in cycles.</summary>
    public Fraction Length => End - Begin;

    /// <summary>The part both arcs cover; false when they do not overlap. A point arc overlaps an arc that contains it.</summary>
    public bool TryIntersect(Arc other, out Arc result)
    {
        var begin = Fraction.Max(Begin, other.Begin);
        var end = Fraction.Min(End, other.End);
        result = new Arc(begin, end);
        if (begin < end) return true;
        if (begin > end) return false;

        // Touching at one instant counts only when an input is a point that lies inside the other (end excluded).
        if (Begin == End && other.Begin == other.End) return Begin == other.Begin;
        if (Begin == End) return other.Begin <= Begin && Begin < other.End;
        if (other.Begin == other.End) return Begin <= other.Begin && other.Begin < End;
        return false;
    }

    /// <summary>Splits the arc at every cycle boundary; a point arc comes back as itself.</summary>
    public CycleSplitter SplitCycles() => new(this);

    /// <summary>The arc as <c>[begin, end)</c>.</summary>
    public override string ToString() => $"[{Begin}, {End})";
}

/// <summary>Walks an <see cref="Arc"/> one cycle at a time without allocating; use it in a <c>foreach</c>.</summary>
public struct CycleSplitter
{
    private readonly Fraction _end;
    private readonly bool _point;
    private Fraction _position;
    private bool _done;

    internal CycleSplitter(Arc arc)
    {
        _position = arc.Begin;
        _end = arc.End;
        _point = arc.Begin == arc.End;
        _done = arc.End < arc.Begin;
        Current = default;
    }

    /// <summary>The piece found by the last <see cref="MoveNext"/>.</summary>
    public Arc Current { get; private set; }

    /// <summary>Returns itself, so the splitter can be used in a <c>foreach</c>.</summary>
    public readonly CycleSplitter GetEnumerator() => this;

    /// <summary>Moves to the next piece, if there is one.</summary>
    /// <returns>True while <see cref="Current"/> holds a piece.</returns>
    public bool MoveNext()
    {
        if (_done) return false;
        if (_point)
        {
            Current = new Arc(_position, _end);
            _done = true;
            return true;
        }

        if (_position >= _end) return false;
        var cycleEnd = new Fraction(_position.Floor() + 1);
        var pieceEnd = Fraction.Min(_end, cycleEnd);
        Current = new Arc(_position, pieceEnd);
        _position = pieceEnd;
        return true;
    }
}

/// <summary>
/// A value placed in time. <see cref="Whole"/> is the full extent of the event; <see cref="Part"/> is the piece of it that
/// falls inside the queried arc (a query that cuts an event in two returns two events sharing a whole).
/// </summary>
public readonly record struct PatternEvent<T>(Arc Whole, Arc Part, T Value)
{
    /// <summary>True when this event starts inside the queried arc, rather than carrying on from before it. Play these.</summary>
    public bool HasOnset => Whole.Begin == Part.Begin;

    internal PatternEvent<T> Mapped(Fraction scale, Fraction offset) =>
        new(new Arc(Whole.Begin * scale + offset, Whole.End * scale + offset),
            new Arc(Part.Begin * scale + offset, Part.End * scale + offset), Value);
}
