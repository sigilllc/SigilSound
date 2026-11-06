using System.Globalization;

namespace Sigil.Sound;

/// <summary>The text given to <see cref="Mini"/> is not valid mini-notation. <see cref="Position"/> is the zero-based column of the problem.</summary>
public sealed class MiniNotationException : FormatException
{
    /// <summary>Creates the exception for a problem at <paramref name="position"/> in <paramref name="text"/>.</summary>
    /// <param name="message">What is wrong.</param>
    /// <param name="position">Zero-based index into <paramref name="text"/> where the problem is.</param>
    /// <param name="text">The mini-notation that was being parsed.</param>
    public MiniNotationException(string message, int position, string text)
        : base($"{message} (column {position + 1} in \"{text}\")")
    {
        Position = position;
    }

    /// <summary>Zero-based index into the text where parsing failed.</summary>
    public int Position { get; }
}

/// <summary>
/// Writing a pattern as text, in the style of Strudel and TidalCycles. The steps of a sequence share one cycle equally.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term><c>a b c</c></term><description>three steps in a cycle</description></item>
/// <item><term><c>a ~ b</c></term><description>a rest (<c>~</c>, or <c>-</c> on its own)</description></item>
/// <item><term><c>a [b c]</c></term><description>a group: b and c share the second step</description></item>
/// <item><term><c>[a,b,c]</c></term><description>layers played together (a chord); commas also work at the top level</description></item>
/// <item><term><c>&lt;a b c&gt;</c></term><description>one per cycle, in turn</description></item>
/// <item><term><c>a*2</c>, <c>a/2</c></term><description>twice as fast, half as fast</description></item>
/// <item><term><c>a(3,8)</c>, <c>a(3,8,2)</c></term><description>a Euclidean rhythm: 3 hits over 8 steps, rotated by 2</description></item>
/// <item><term><c>a@3</c>, <c>a _ _</c></term><description>take 3 shares of the cycle instead of 1</description></item>
/// <item><term><c>a!3</c>, <c>a !</c></term><description>repeat the step (three in all, and once more)</description></item>
/// <item><term><c>a?</c>, <c>a?0.3</c></term><description>drop it at random (half the time, or 30%), the same way each time</description></item>
/// </list>
/// </remarks>
public static class Mini
{
    /// <summary>
    /// Notes by name, such as <c>"c3 [e3 g3]*2 ~ &lt;b3 d4&gt;"</c>. Names are case-insensitive, sharps are <c>#</c> and flats <c>b</c>
    /// (<c>eb4</c>), and a name without an octave is in octave 3. Add <c>:0.6</c> for the velocity (<c>c4:0.6</c>).
    /// </summary>
    /// <exception cref="MiniNotationException">The text is not valid.</exception>
    public static Pattern<Note> Notes(string text) => Parse(text, ParseNote);

    /// <summary>Whole numbers, such as scale degrees: <c>"0 2 &lt;4 -1&gt; ~"</c>. Pair with <see cref="Scale.Notes"/>.</summary>
    /// <exception cref="MiniNotationException">The text is not valid, or a word is not a whole number.</exception>
    public static Pattern<int> Integers(string text) => Parse(text, ParseInteger);

    /// <summary>
    /// Chords by name, such as <c>"c3:min7 f3:7 [g3:sus4 g3]"</c>; see <see cref="Chord.Parse"/> for the names. Turn them into notes with
    /// <see cref="ChordPatterns.Notes"/> or <see cref="ChordPatterns.Arp"/>.
    /// </summary>
    /// <exception cref="MiniNotationException">The text is not valid.</exception>
    public static Pattern<Chord> Chords(string text) => Parse(text, Chord.Parse);

    /// <summary>Numbers, such as <c>"400 800 1600 3200"</c>, for driving a parameter.</summary>
    /// <exception cref="MiniNotationException">The text is not valid.</exception>
    public static Pattern<double> Numbers(string text) => Parse(text, ParseNumber);

    /// <summary>Parses mini-notation whose words become values through <paramref name="atom"/>; throw <see cref="FormatException"/> for a word it does not accept.</summary>
    /// <exception cref="MiniNotationException">The text is not valid.</exception>
    public static Pattern<T> Parse<T>(string text, Func<string, T> atom)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(atom);
        return new MiniParser<T>(text, atom).ParseAll();
    }

    private static Note ParseNote(string word)
    {
        var parts = word.Split(':');
        if (parts.Length > 2) throw new FormatException($"'{word}' has more than one velocity.");

        string name = parts[0];
        if (name.Length == 0) throw new FormatException("A note needs a name.");
        // A name with no octave number means octave 3.
        if (!char.IsDigit(name[^1])) name += "3";

        var note = new Note(global::Sigil.Sound.Notes.ParsePitch(name));
        if (parts.Length == 2)
        {
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float velocity) || velocity is < 0 or > 1)
                throw new FormatException($"'{parts[1]}' is not a velocity between 0 and 1.");
            note = note with { Velocity = velocity };
        }

        return note;
    }

    // A note name with its octave defaulting to 3, as in Mini.Notes.
    internal static int ParsePitchName(string word) => ParseNote(word).Pitch;

    private static int ParseInteger(string word) =>
        int.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new FormatException($"'{word}' is not a whole number.");

    private static double ParseNumber(string word) =>
        double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
            ? value
            : throw new FormatException($"'{word}' is not a number.");
}

internal sealed class MiniParser<T>(string text, Func<string, T> atom)
{
    // A typo like a*100000000 would otherwise ask the engine for a hundred million events a cycle.
    private const int MaxCount = 1024;

    // Brackets nested deeper than this are refused, which keeps the parser's recursion shallow however the text is written.
    private const int MaxDepth = 32;
    private int _depth;

    private int _position;
    private int _seed;

    private sealed class Step(Pattern<T> pattern, Fraction weight)
    {
        public Pattern<T> Pattern { get; set; } = pattern;
        public Fraction Weight { get; set; } = weight;
    }

    public Pattern<T> ParseAll()
    {
        var pattern = ParseLayers('\0', alternate: false);
        SkipSpace();
        if (_position < text.Length)
            throw Error($"Unexpected '{text[_position]}'");
        return pattern;
    }

    // Layers separated by commas, all played together.
    private Pattern<T> ParseLayers(char closer, bool alternate)
    {
        var layers = new List<Pattern<T>> { ParseSequence(closer, alternate) };
        while (Peek() == ',')
        {
            _position++;
            layers.Add(ParseSequence(closer, alternate));
        }

        return layers.Count == 1 ? layers[0] : Pat.Stack(layers.ToArray());
    }

    private Pattern<T> ParseSequence(char closer, bool alternate)
    {
        var steps = new List<Step>();
        while (true)
        {
            SkipSpace();
            if (_position >= text.Length)
            {
                if (closer != '\0') throw Error($"Missing '{closer}'");
                break;
            }

            char c = text[_position];
            if (c == ',' || c == closer) break;
            if (c is ']' or '>' or ')') throw Error($"Unexpected '{c}'");

            if (c == '_')
            {
                if (steps.Count == 0) throw Error("'_' needs a step before it to extend");
                steps[^1].Weight += Fraction.One;
                _position++;
            }
            else if (c == '!')
            {
                if (steps.Count == 0) throw Error("'!' needs a step before it to repeat");
                steps.Add(new Step(steps[^1].Pattern, steps[^1].Weight));
                _position++;
            }
            else
            {
                steps.AddRange(ParseStep());
            }
        }

        if (alternate)
            return Pat.Cat(steps.Select(s => s.Pattern).ToArray());
        return Pat.Weighted(steps.Select(s => (s.Pattern, s.Weight)).ToArray());
    }

    private List<Step> ParseStep()
    {
        var pattern = ParseTerm();
        var weight = Fraction.One;
        int copies = 1;

        // Modifiers hug the term: "a*2" is one step, "a *2" is an error.
        while (_position < text.Length)
        {
            int at = _position;
            char c = text[_position];
            if (c == '*' || c == '/')
            {
                _position++;
                var factor = ParseFraction();
                if (factor <= Fraction.Zero || factor > MaxCount || factor.Reciprocal() > MaxCount) throw Error($"The speed factor must be above zero and at most {MaxCount}", at);
                pattern = c == '*' ? pattern.Fast(factor) : pattern.Slow(factor);
            }
            else if (c == '(')
            {
                _position++;
                int pulses = ParseInteger();
                Expect(',');
                int steps = ParseInteger();
                int rotation = 0;
                SkipSpace();
                if (Peek() == ',')
                {
                    _position++;
                    rotation = ParseInteger();
                }
                Expect(')');
                if (steps > MaxCount) throw Error($"A rhythm can have at most {MaxCount} steps", at);
                if (steps < 1 || pulses < 0 || pulses > steps) throw Error($"({pulses},{steps}) is not a valid rhythm: pulses must be between 0 and steps", at);
                pattern = pattern.Euclid(pulses, steps, rotation);
            }
            else if (c == '@')
            {
                _position++;
                weight = ParseFraction();
                if (weight <= Fraction.Zero) throw Error("The step weight must be above zero", at);
            }
            else if (c == '!')
            {
                _position++;
                copies = char.IsDigit(Peek()) ? ParseInteger() : 2;
                if (copies < 1 || copies > MaxCount) throw Error($"A repeat count must be between 1 and {MaxCount}", at);
            }
            else if (c == '?')
            {
                _position++;
                double amount = Peek() is >= '0' and <= '9' or '.' ? ParseDecimal() : 0.5;
                if (amount > 1) throw Error("A drop chance must be between 0 and 1", at);
                pattern = pattern.Degrade(amount, _seed++);
            }
            else
            {
                break;
            }
        }

        var steps2 = new List<Step>(copies);
        for (int i = 0; i < copies; i++)
            steps2.Add(new Step(pattern, weight));
        return steps2;
    }

    private Pattern<T> ParseTerm()
    {
        int start = _position;
        char c = text[_position];
        if (c is '[' or '<' && ++_depth > MaxDepth)
            throw Error($"Brackets are nested more than {MaxDepth} deep");

        if (c == '[')
        {
            _position++;
            var inner = ParseLayers(']', alternate: false);
            Expect(']');
            _depth--;
            return inner;
        }

        if (c == '<')
        {
            _position++;
            var inner = ParseLayers('>', alternate: true);
            Expect('>');
            _depth--;
            return inner;
        }

        bool isRest = c == '~' || (c == '-' && !(_position + 1 < text.Length && (char.IsDigit(text[_position + 1]) || text[_position + 1] == '.')));
        if (isRest)
        {
            _position++;
            return Pat.Silence<T>();
        }

        while (_position < text.Length && IsWordChar(text[_position]))
            _position++;
        if (_position == start)
            throw Error($"Unexpected '{c}'");

        string word = text[start.._position];
        try
        {
            return Pat.Pure(atom(word));
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            throw new MiniNotationException(e.Message, start, text);
        }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '#' or '.' or '-' or '+' or ':';

    private double ParseDecimal()
    {
        int start = _position;
        while (_position < text.Length && (char.IsDigit(text[_position]) || text[_position] == '.'))
            _position++;
        if (!double.TryParse(text.AsSpan(start.._position), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw Error("Expected a number", start);
        return value;
    }

    private Fraction ParseFraction()
    {
        int start = _position;
        double value = ParseDecimal();
        try
        {
            return Fraction.FromDouble(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Error("That number is too large", start);
        }
    }

    private int ParseInteger()
    {
        SkipSpace();
        int start = _position;
        while (_position < text.Length && char.IsDigit(text[_position]))
            _position++;
        if (!int.TryParse(text.AsSpan(start.._position), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            throw Error("Expected a whole number", start);
        return value;
    }

    private void Expect(char expected)
    {
        SkipSpace();
        if (Peek() != expected)
            throw Error(_position >= text.Length ? $"Missing '{expected}'" : $"Expected '{expected}' but found '{text[_position]}'");
        _position++;
    }

    private char Peek() => _position < text.Length ? text[_position] : '\0';

    private void SkipSpace()
    {
        while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            _position++;
    }

    private MiniNotationException Error(string message, int? at = null) => new(message, at ?? _position, text);
}
