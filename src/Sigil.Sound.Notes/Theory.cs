namespace Sigil.Sound;

/// <summary>
/// A scale: a root note and the steps above it. Degrees count up the scale from 0 and run past the octave, so degree 7 of a
/// seven-note scale is the root again an octave up, and negative degrees go below the root.
/// </summary>
/// <example>
/// <code>
/// // A melody by scale degree. 48 is C3; degree 0 is 48, degree 2 is 51 and degree 7 is the octave, 60.
/// var scale = Scale.Minor(48);
/// scale.Notes(Mini.Integers("0 2 &lt;4 7&gt; ~"))
///     .Play(v =&gt; Saw(v.Frequency).LowPass(2000).Adsr(0.005, 0.1, 0.5, 0.2), bpm: 110) * 0.3
/// </code>
/// </example>
public sealed class Scale
{
    private static readonly Dictionary<string, int[]> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["major"] = [0, 2, 4, 5, 7, 9, 11],
        ["ionian"] = [0, 2, 4, 5, 7, 9, 11],
        ["minor"] = [0, 2, 3, 5, 7, 8, 10],
        ["aeolian"] = [0, 2, 3, 5, 7, 8, 10],
        ["harmonicminor"] = [0, 2, 3, 5, 7, 8, 11],
        ["melodicminor"] = [0, 2, 3, 5, 7, 9, 11],
        ["dorian"] = [0, 2, 3, 5, 7, 9, 10],
        ["phrygian"] = [0, 1, 3, 5, 7, 8, 10],
        ["lydian"] = [0, 2, 4, 6, 7, 9, 11],
        ["mixolydian"] = [0, 2, 4, 5, 7, 9, 10],
        ["locrian"] = [0, 1, 3, 5, 6, 8, 10],
        ["majorpentatonic"] = [0, 2, 4, 7, 9],
        ["minorpentatonic"] = [0, 3, 5, 7, 10],
        ["blues"] = [0, 3, 5, 6, 7, 10],
        ["wholetone"] = [0, 2, 4, 6, 8, 10],
        ["diminished"] = [0, 1, 3, 4, 6, 7, 9, 10],
        ["chromatic"] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
    };

    private readonly int[] _steps;
    private readonly int _pitchClasses; // bit n is set when pitch class n belongs to the scale

    /// <summary>A scale from a root pitch and the semitone steps above it, which must rise from 0 and stay within one octave.</summary>
    /// <param name="root">The MIDI pitch of degree 0.</param>
    /// <param name="steps">Semitones above the root for each degree: 0 first, strictly rising, all below 12.</param>
    /// <param name="name">A label.</param>
    public Scale(int root, IEnumerable<int> steps, string name = "custom")
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps = steps.ToArray();
        if (_steps.Length == 0 || _steps[0] != 0)
            throw new ArgumentException("A scale needs at least one step and must start at 0.", nameof(steps));
        for (int i = 1; i < _steps.Length; i++)
            if (_steps[i] <= _steps[i - 1] || _steps[i] >= 12)
                throw new ArgumentException("Scale steps must rise and stay within one octave.", nameof(steps));

        Root = root;
        Name = name;
        foreach (int step in _steps)
            _pitchClasses |= 1 << (((root + step) % 12) + 12) % 12;
    }

    /// <summary>
    /// True when <paramref name="pitch"/> is a tone of the scale in some octave. It reads a precomputed set and allocates nothing, so it
    /// is fit to ask of every note as it plays.
    /// </summary>
    /// <param name="pitch">A MIDI pitch.</param>
    public bool Contains(int pitch) => (_pitchClasses & (1 << (((pitch % 12) + 12) % 12))) != 0;

    /// <summary>The MIDI pitch of degree 0.</summary>
    public int Root { get; }

    /// <summary>The scale's name, such as "minor".</summary>
    public string Name { get; }

    /// <summary>How many degrees one octave holds.</summary>
    public int Count => _steps.Length;

    /// <summary>The MIDI pitch of a degree. Degrees past the end climb an octave per <see cref="Count"/>; negative ones go down.</summary>
    public int this[int degree]
    {
        get
        {
            long octave = Pat.FloorDiv(degree, _steps.Length);
            int index = (int)(degree - octave * _steps.Length);
            return checked((int)(Root + _steps[index] + 12 * octave));
        }
    }

    /// <summary>The note on a degree.</summary>
    public Note Note(int degree, float velocity = 0.8f) => new(this[degree], velocity);

    /// <summary>The notes on the given degrees, in order: <c>Scale.Minor(C3).Degrees(0, 2, 4, 7)</c>.</summary>
    public IReadOnlyList<Note> Degrees(params int[] degrees)
    {
        ArgumentNullException.ThrowIfNull(degrees);
        return degrees.Select(d => Note(d)).ToArray();
    }

    /// <summary>The notes on the degrees written as whole numbers in <see cref="Mini"/> notation: <c>Scale.Parse("c3 minor").Degrees("0 2 4 7")</c>.</summary>
    /// <exception cref="MiniNotationException">The text is not valid.</exception>
    public IReadOnlyList<Note> Degrees(string degrees)
    {
        ArgumentNullException.ThrowIfNull(degrees);
        return Mini.Integers(degrees).Cycle(0).Select(e => Note(e.Value)).ToArray();
    }

    /// <summary>
    /// Turns degrees written in <see cref="Mini"/> notation into a pattern of notes, so the scale and the melody are both text:
    /// <c>Scale.Parse("c3 minor").Notes("0 2 &lt;4 7&gt; ~")</c>.
    /// </summary>
    /// <exception cref="MiniNotationException">The text is not valid.</exception>
    public Pattern<Note> Notes(string degrees)
    {
        ArgumentNullException.ThrowIfNull(degrees);
        return Notes(Mini.Integers(degrees));
    }

    /// <summary>Turns a pattern of degrees into a pattern of notes.</summary>
    public Pattern<Note> Notes(Pattern<int> degrees)
    {
        ArgumentNullException.ThrowIfNull(degrees);
        return degrees.Select(d => Note(d));
    }

    /// <summary>This scale started on another root.</summary>
    public Scale WithRoot(int root) => new(root, _steps, Name);

    /// <summary>Major (Ionian): 0 2 4 5 7 9 11.</summary>
    public static Scale Major(int root) => Create(root, "major");

    /// <summary>Natural minor (Aeolian): 0 2 3 5 7 8 10.</summary>
    public static Scale Minor(int root) => Create(root, "minor");

    /// <summary>Harmonic minor: natural minor with a raised seventh.</summary>
    public static Scale HarmonicMinor(int root) => Create(root, "harmonicminor");

    /// <summary>Melodic minor (ascending): minor with raised sixth and seventh.</summary>
    public static Scale MelodicMinor(int root) => Create(root, "melodicminor");

    /// <summary>Dorian: minor with a raised sixth.</summary>
    public static Scale Dorian(int root) => Create(root, "dorian");

    /// <summary>Phrygian: minor with a lowered second.</summary>
    public static Scale Phrygian(int root) => Create(root, "phrygian");

    /// <summary>Lydian: major with a raised fourth.</summary>
    public static Scale Lydian(int root) => Create(root, "lydian");

    /// <summary>Mixolydian: major with a lowered seventh.</summary>
    public static Scale Mixolydian(int root) => Create(root, "mixolydian");

    /// <summary>Locrian: minor with lowered second and fifth.</summary>
    public static Scale Locrian(int root) => Create(root, "locrian");

    /// <summary>Major pentatonic: 0 2 4 7 9.</summary>
    public static Scale MajorPentatonic(int root) => Create(root, "majorpentatonic");

    /// <summary>Minor pentatonic: 0 3 5 7 10.</summary>
    public static Scale MinorPentatonic(int root) => Create(root, "minorpentatonic");

    /// <summary>The blues scale: minor pentatonic with a flat fifth.</summary>
    public static Scale Blues(int root) => Create(root, "blues");

    /// <summary>Whole tone: six notes a whole step apart.</summary>
    public static Scale WholeTone(int root) => Create(root, "wholetone");

    /// <summary>Every semitone.</summary>
    public static Scale Chromatic(int root) => Create(root, "chromatic");

    /// <summary>
    /// A scale by name: <c>"c3 minor"</c>, <c>"F#2 dorian"</c> or <c>"c3:minor"</c>. A root without an octave is in octave 3.
    /// Names are case-insensitive; spaces, dashes and underscores inside a name are ignored (<c>"minor pentatonic"</c> works).
    /// </summary>
    /// <exception cref="FormatException">The text is not a root and a known scale.</exception>
    public static Scale Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (root, name) = SplitRootAndName(text);
        return Create(root, name);
    }

    private static Scale Create(int root, string name)
    {
        string key = new([.. name.Where(char.IsLetter)]);
        if (!Known.TryGetValue(key, out var steps))
            throw new FormatException($"'{name}' is not a known scale. Known: {string.Join(", ", Known.Keys.Order())}.");
        return new Scale(root, steps, key.ToLowerInvariant());
    }

    internal static (int Root, string Name) SplitRootAndName(string text)
    {
        var parts = text.Split([' ', ':'], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            throw new FormatException($"'{text}' needs a root and a name, such as \"c3 minor\".");
        return (Mini.ParsePitchName(parts[0]), parts[1]);
    }

    /// <summary>The scale as text, such as <c>C major</c>.</summary>
    public override string ToString() => $"{Sigil.Sound.Notes.NameOf(Root)} {Name}";
}

/// <summary>How an arpeggio walks through a chord.</summary>
public enum ArpMode
{
    /// <summary>Low to high.</summary>
    Up,

    /// <summary>High to low.</summary>
    Down,

    /// <summary>Up, then back down without repeating the top or bottom note.</summary>
    UpDown,

    /// <summary>Down, then back up without repeating the ends.</summary>
    DownUp,

    /// <summary>Alternating from the outside in: lowest, highest, second lowest, second highest, ...</summary>
    Converge,
}

/// <summary>
/// A chord: a set of pitches, kept low to high. Make one by name (<c>Chord.Parse("c3:min7")</c>), from a root
/// (<c>Chord.Minor(C3)</c>), or from the pitches themselves.
/// </summary>
public sealed class Chord
{
    // Case matters for "M" (major) against "m" (minor); anything else is matched in lower case as well.
    private static readonly Dictionary<string, int[]> Known = new()
    {
        ["major"] = [0, 4, 7], ["maj"] = [0, 4, 7], ["M"] = [0, 4, 7],
        ["minor"] = [0, 3, 7], ["min"] = [0, 3, 7], ["m"] = [0, 3, 7],
        ["dim"] = [0, 3, 6], ["diminished"] = [0, 3, 6],
        ["aug"] = [0, 4, 8], ["augmented"] = [0, 4, 8],
        ["sus2"] = [0, 2, 7], ["sus4"] = [0, 5, 7], ["sus"] = [0, 5, 7],
        ["5"] = [0, 7], ["power"] = [0, 7],
        ["6"] = [0, 4, 7, 9], ["m6"] = [0, 3, 7, 9], ["min6"] = [0, 3, 7, 9],
        ["7"] = [0, 4, 7, 10], ["dom7"] = [0, 4, 7, 10],
        ["maj7"] = [0, 4, 7, 11], ["M7"] = [0, 4, 7, 11],
        ["m7"] = [0, 3, 7, 10], ["min7"] = [0, 3, 7, 10],
        ["mmaj7"] = [0, 3, 7, 11], ["minmaj7"] = [0, 3, 7, 11],
        ["m7b5"] = [0, 3, 6, 10], ["halfdim"] = [0, 3, 6, 10],
        ["dim7"] = [0, 3, 6, 9],
        ["9"] = [0, 4, 7, 10, 14], ["dom9"] = [0, 4, 7, 10, 14],
        ["maj9"] = [0, 4, 7, 11, 14], ["m9"] = [0, 3, 7, 10, 14], ["min9"] = [0, 3, 7, 10, 14],
        ["add9"] = [0, 4, 7, 14],
    };

    /// <summary>A chord of exactly these MIDI pitches (any order; they are sorted).</summary>
    public Chord(IEnumerable<int> pitches, string name = "custom")
    {
        ArgumentNullException.ThrowIfNull(pitches);
        Pitches = pitches.Distinct().Order().ToArray();
        if (Pitches.Count == 0) throw new ArgumentException("A chord needs at least one pitch.", nameof(pitches));
        Name = name;
        // Built once here, so turning a chord into notes while a pattern plays allocates nothing.
        Notes = Pitches.Select(p => new Note(p)).ToArray();
    }

    /// <summary>The MIDI pitches, low to high.</summary>
    public IReadOnlyList<int> Pitches { get; }

    /// <summary>The same pitches as notes at the default velocity.</summary>
    public IReadOnlyList<Note> Notes { get; }

    /// <summary>A label such as "min7".</summary>
    public string Name { get; }

    /// <summary>The chord built on a root from a quality name such as <c>"min7"</c>; see <see cref="Parse"/> for the names.</summary>
    public static Chord Of(int root, string quality)
    {
        ArgumentNullException.ThrowIfNull(quality);
        string key = quality.Trim();
        if (!Known.TryGetValue(key, out var steps) && !Known.TryGetValue(key.ToLowerInvariant(), out steps))
            throw new FormatException($"'{quality}' is not a known chord. Known: {string.Join(", ", Known.Keys.Order())}.");
        return new Chord(steps.Select(s => root + s), quality.Trim());
    }

    /// <summary>A major triad on <paramref name="root"/> (a MIDI pitch).</summary>
    public static Chord Major(int root) => Of(root, "major");

    /// <summary>A minor triad on <paramref name="root"/>.</summary>
    public static Chord Minor(int root) => Of(root, "minor");

    /// <summary>A diminished triad on <paramref name="root"/>.</summary>
    public static Chord Diminished(int root) => Of(root, "dim");

    /// <summary>An augmented triad on <paramref name="root"/>.</summary>
    public static Chord Augmented(int root) => Of(root, "aug");

    /// <summary>A suspended second chord on <paramref name="root"/>.</summary>
    public static Chord Sus2(int root) => Of(root, "sus2");

    /// <summary>A suspended fourth chord on <paramref name="root"/>.</summary>
    public static Chord Sus4(int root) => Of(root, "sus4");

    /// <summary>A dominant seventh chord on <paramref name="root"/>.</summary>
    public static Chord Dominant7(int root) => Of(root, "7");

    /// <summary>A major seventh chord on <paramref name="root"/>.</summary>
    public static Chord Major7(int root) => Of(root, "maj7");

    /// <summary>A minor seventh chord on <paramref name="root"/>.</summary>
    public static Chord Minor7(int root) => Of(root, "min7");

    /// <summary>
    /// A chord by name: a root and a quality joined by a colon, such as <c>"c3:min7"</c> or <c>"F#2:sus4"</c>. A root without an
    /// octave is in octave 3 and no quality means major. Qualities: major (maj, M), minor (min, m), dim, aug, sus2, sus4, 5, 6, m6,
    /// 7, maj7, m7, mmaj7, m7b5, dim7, 9, maj9, m9, add9.
    /// </summary>
    /// <exception cref="FormatException">The text is not a root and a known quality.</exception>
    public static Chord Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int colon = text.IndexOf(':');
        string rootText = colon < 0 ? text : text[..colon];
        string quality = colon < 0 ? "major" : text[(colon + 1)..];
        return Of(Mini.ParsePitchName(rootText.Trim()), quality);
    }

    /// <summary>Raises the lowest note an octave, <paramref name="count"/> times (or lowers the highest, if negative): a first inversion has the third on the bottom.</summary>
    public Chord Inversion(int count)
    {
        if (Math.Abs(count) > 32) throw new ArgumentOutOfRangeException(nameof(count), count, "An inversion count must be between -32 and 32.");
        var pitches = Pitches.ToList();
        for (int i = 0; i < Math.Abs(count); i++)
        {
            if (count > 0)
            {
                int low = pitches.Min();
                pitches.Remove(low);
                pitches.Add(low + 12);
            }
            else
            {
                int high = pitches.Max();
                pitches.Remove(high);
                pitches.Add(high - 12);
            }
        }
        return new Chord(pitches, Name);
    }

    /// <summary>Moves every note by <paramref name="semitones"/>.</summary>
    public Chord Transpose(int semitones) => new(Pitches.Select(p => p + semitones), Name);

    /// <summary>The notes one after another as a repeating pattern, each taking an equal share of the cycle.</summary>
    public Pattern<Note> Arp(ArpMode mode = ArpMode.Up, int octaves = 1) => Pat.Pure(this).Arp(mode, octaves);

    /// <summary>All the notes at once, filling the cycle.</summary>
    public Pattern<Note> ToPattern() => Pat.Pure(this).Notes();

    /// <summary>
    /// Plays steps of this chord: step 0 is its lowest tone, 1 the next one up, and so on; past the top it climbs into the next octave, and
    /// below 0 it goes down below the lowest tone. <c>Chord.Major(60).Steps("0 1 2 3")</c> is C E G and the C above.
    /// A step whose note would leave the MIDI range is left out.
    /// </summary>
    /// <param name="steps">The steps, such as <c>Mini.Integers("7 6 5 4 3 2 1 0")</c>, a fall through two octaves of a triad.</param>
    public Pattern<Note> Steps(Pattern<int> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var pitches = Pitches;
        return steps.Select(step => new Note(StepPitch(pitches, step))).Where(note => note.Pitch is >= 0 and <= 127);
    }

    /// <summary>Plays steps of this chord written in <see cref="Mini"/> notation; see <see cref="Steps(Pattern{int})"/>.</summary>
    /// <param name="steps">The steps, such as <c>"0 2 4 [7 5]"</c>.</param>
    /// <exception cref="MiniNotationException">The text is not valid.</exception>
    public Pattern<Note> Steps(string steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return Steps(Mini.Integers(steps));
    }

    // The pitch of a step of a chord: wraps up an octave per chord size, and down for negative steps.
    internal static int StepPitch(IReadOnlyList<int> pitches, int step)
    {
        int count = pitches.Count;
        int octaves = (int)Math.Floor(step / (double)count);
        return pitches[step - octaves * count] + 12 * octaves;
    }

    /// <summary>The chord as text: its pitches and its quality.</summary>
    public override string ToString() => $"{string.Join(" ", Pitches.Select(Sigil.Sound.Notes.NameOf))} ({Name})";

    // The position of step s of an arpeggio over `count` ascending pitches and `octaves` octaves, as an index counted from the
    // bottom of the whole run. Pure arithmetic, so playing an arpeggio needs no lists.
    internal static int ArpLength(ArpMode mode, int count, int octaves)
    {
        int run = count * octaves;
        return mode is ArpMode.UpDown or ArpMode.DownUp ? Math.Max(1, 2 * run - 2) : run;
    }

    internal static int ArpIndex(ArpMode mode, int count, int octaves, int step)
    {
        int run = count * octaves;
        return mode switch
        {
            ArpMode.Up => step,
            ArpMode.Down => run - 1 - step,
            ArpMode.UpDown => step < run ? step : 2 * run - 2 - step,
            ArpMode.DownUp => run - 1 - (step < run ? step : 2 * run - 2 - step),
            ArpMode.Converge => step % 2 == 0 ? step / 2 : run - 1 - step / 2,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown arpeggio mode."),
        };
    }
}

/// <summary>Patterns of chords: playing them as block chords, or as arpeggios.</summary>
public static class ChordPatterns
{
    /// <summary>
    /// Plays steps of whatever chord is in force at each step: step 0 is the chord's lowest tone, 1 the next one up, past the top it climbs
    /// into the next octave, below 0 it goes down. The rhythm is the steps' own; the chords only say which notes they are, so the same
    /// text follows the harmony, whether it is written (<c>Mini.Chords("&lt;c3:min7 f3:7&gt;")</c>) or played
    /// (<see cref="NoteStream.Chord"/>). A step with no chord, or a note outside the MIDI range, is left out.
    /// </summary>
    /// <param name="chords">The chords.</param>
    /// <param name="steps">The steps, such as <c>Mini.Integers("7 6 5 4 3 2 1 0")</c>.</param>
    /// <example>
    /// <code>
    /// Mini.Chords("&lt;c3:min7 f3:7&gt;").Steps(Mini.Integers("[7 6 5 4 3 2 1 0] ~ [4 2 0] ~")).Play(v => Saw(v.Frequency).Adsr(0.003, 0.2, 0, 0.2))
    /// </code>
    /// </example>
    public static Pattern<Note> Steps(this Pattern<Chord> chords, Pattern<int> steps)
    {
        ArgumentNullException.ThrowIfNull(chords);
        ArgumentNullException.ThrowIfNull(steps);
        var stepCache = new List<PatternEvent<int>>();
        var chordCache = new List<PatternEvent<Chord>>();
        return new((arc, output) =>
        {
            var stepBuffer = Interlocked.Exchange(ref stepCache, null!) ?? new List<PatternEvent<int>>();
            var chordBuffer = Interlocked.Exchange(ref chordCache, null!) ?? new List<PatternEvent<Chord>>();
            steps.QueryInto(arc, stepBuffer);
            foreach (var step in stepBuffer)
            {
                chordBuffer.Clear();
                chords.QueryInto(new Arc(step.Whole.Begin, step.Whole.End), chordBuffer);

                // The chord whose event holds the step's start.
                Chord? chord = null;
                foreach (var candidate in chordBuffer)
                {
                    if (candidate.Whole.Begin <= step.Whole.Begin && step.Whole.Begin < candidate.Whole.End)
                    {
                        chord = candidate.Value;
                        break;
                    }
                }

                if (chord is null)
                    continue;
                int pitch = Chord.StepPitch(chord.Pitches, step.Value);
                if (pitch is >= 0 and <= 127)
                    output.Add(new PatternEvent<Note>(step.Whole, step.Part, new Note(pitch)));
            }

            stepBuffer.Clear();
            chordBuffer.Clear();
            stepCache = stepBuffer;
            chordCache = chordBuffer;
        });
    }

    /// <summary>Plays steps of the chords written in <see cref="Mini"/> notation; see <see cref="Steps(Pattern{Chord}, Pattern{int})"/>.</summary>
    /// <param name="chords">The chords.</param>
    /// <param name="steps">The steps, such as <c>"0 2 4 [7 5]"</c>.</param>
    /// <exception cref="MiniNotationException">The text is not valid.</exception>
    public static Pattern<Note> Steps(this Pattern<Chord> chords, string steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return chords.Steps(Mini.Integers(steps));
    }

    /// <summary>The lowest tone of each chord, as a pattern of pitches: the bass line of a progression, or of the chord being played.</summary>
    /// <param name="chords">The chords.</param>
    public static Pattern<int> Lowest(this Pattern<Chord> chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        return chords.Select(chord => chord.Pitches[0]);
    }

    /// <summary>The highest tone of each chord, as a pattern of pitches.</summary>
    /// <param name="chords">The chords.</param>
    public static Pattern<int> Highest(this Pattern<Chord> chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        return chords.Select(chord => chord.Pitches[^1]);
    }

    /// <summary>Every chord as its notes, all sounding together for the chord's whole event.</summary>
    public static Pattern<Note> Notes(this Pattern<Chord> chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        return chords.SelectMany(c => c.Notes);
    }

    /// <summary>
    /// Plays each chord one note after another, dividing the chord's event equally among the steps, so a chord that lasts a bar
    /// becomes an arpeggio across the bar. Speed it up with <c>Fast</c> or add the octaves.
    /// </summary>
    /// <param name="chords">The chords, such as <c>Mini.Chords("c3:min7 f3:7")</c>.</param>
    /// <param name="mode">The order the notes are walked in.</param>
    /// <param name="octaves">How many octaves to climb (1 to 4); more octaves make a longer run.</param>
    public static Pattern<Note> Arp(this Pattern<Chord> chords, ArpMode mode = ArpMode.Up, int octaves = 1)
    {
        ArgumentNullException.ThrowIfNull(chords);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown arpeggio mode.");
        if (octaves is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(octaves), octaves, "Octaves must be between 1 and 4.");

        var cache = new List<PatternEvent<Chord>>();
        return new((arc, output) =>
        {
            var buffer = Interlocked.Exchange(ref cache, null!) ?? new List<PatternEvent<Chord>>();
            chords.QueryInto(arc, buffer);
            foreach (var e in buffer)
            {
                var pitches = e.Value.Pitches;
                int count = pitches.Count;
                int steps = Chord.ArpLength(mode, count, octaves);
                var stepLength = e.Whole.Length / steps;
                for (int s = 0; s < steps; s++)
                {
                    var begin = e.Whole.Begin + stepLength * s;
                    var whole = new Arc(begin, begin + stepLength);
                    if (!e.Part.TryIntersect(whole, out var part)) continue;

                    int index = Chord.ArpIndex(mode, count, octaves, s);
                    int pitch = pitches[index % count] + 12 * (index / count);
                    output.Add(new PatternEvent<Note>(whole, part, new Note(pitch)));
                }
            }
            buffer.Clear();
            cache = buffer;
        });
    }
}
