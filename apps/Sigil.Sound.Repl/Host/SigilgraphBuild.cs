using Sigil.Sound;
using Sigil.Sound.Repl;
using static Sigil.Sound.Sounds;

namespace SigilgraphHost;

/// <summary>A snippet asked for something the simulated device cannot give it. The message says what and what to do instead.</summary>
internal sealed class SigilgraphException(string message) : Exception(message);

/// <summary>The device's fixed numbers, copied from the Workbench so a snippet that fits here fits there. A test compares them.</summary>
internal static class SigilgraphLimits
{
    public const int ControlInputs = 4;
    public const int ControlOutputs = 2;
    public const int MaxPolyphony = 32;
    public const int MaxNameLength = 24;
}

/// <summary>
/// The record of one run of a snippet in Sigilgraph mode: the controls it asked for and the outputs it set.
/// <see cref="SigilgraphApi"/> writes into the build that is current on the running thread, and the session keeps the result
/// only when the run succeeded, so a snippet with an error changes nothing on the page.
/// </summary>
internal sealed class SigilgraphBuild
{
    [ThreadStatic]
    private static SigilgraphBuild? _current;

    private readonly SigilgraphSession _session;
    private readonly List<Control> _declared = [];
    private readonly Dictionary<string, Sound> _sounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _assets = [];
    private readonly bool[] _controlInputsUsed = new bool[SigilgraphLimits.ControlInputs];
    private readonly bool[] _controlOutputsUsed = new bool[SigilgraphLimits.ControlOutputs];

    internal SigilgraphBuild(SigilgraphSession session)
    {
        _session = session;
        Input = new InputApi(this);
        Output = new OutputApi(this);
    }

    internal InputApi Input { get; }

    internal OutputApi Output { get; }

    /// <summary>The controls this run declared, in the order the snippet declared them.</summary>
    internal IReadOnlyList<Control> Declared => _declared;

    internal IReadOnlyList<string> Assets => _assets;

    internal IReadOnlyList<bool> ControlInputsUsed => _controlInputsUsed;

    internal IReadOnlyList<bool> ControlOutputsUsed => _controlOutputsUsed;

    internal bool UsesAudioInput { get; private set; }

    internal bool UsesNoteInput { get; private set; }

    internal Pattern? NotesOut { get; private set; }

    internal Pattern<Note>? LiveNotesOut { get; private set; }

    internal NoteStream? StreamNotesOut { get; private set; }

    internal NoteStream NotesInput
    {
        get
        {
            UsesNoteInput = true;
            return _session.Keys.Stream;
        }
    }

    internal static SigilgraphBuild Require() =>
        _current ?? throw new SigilgraphException("This can only be used while a Sound Script runs; it is not available from other code.");

    internal static Scope Enter(SigilgraphBuild build)
    {
        var previous = _current;
        _current = build;
        return new Scope(previous);
    }

    internal readonly struct Scope(SigilgraphBuild? previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }

    internal Sound AudioInput
    {
        get
        {
            UsesAudioInput = true;
            return _session.AudioInput;
        }
    }

    internal Sound ControlInput(int index)
    {
        CheckIndex(index, SigilgraphLimits.ControlInputs, "control input");
        _controlInputsUsed[index] = true;
        return _session.ControlInputSound(index);
    }

    internal void SetControlOutput(int index, Sound sound)
    {
        CheckIndex(index, SigilgraphLimits.ControlOutputs, "control output");
        ArgumentNullException.ThrowIfNull(sound);
        _controlOutputsUsed[index] = true;
    }

    internal void SetNotesOutput(Pattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        NotesOut = pattern;
        LiveNotesOut = null; // only the last call counts
        StreamNotesOut = null;
    }

    internal void SetStreamNotesOutput(NoteStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        StreamNotesOut = stream;
        NotesOut = null; // only the last call counts
        LiveNotesOut = null;
    }

    internal void SetLiveNotesOutput(Pattern<Note> pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        try
        {
            pattern.Validate();
        }
        catch (InvalidOperationException e)
        {
            throw new SigilgraphException($"Out.Notes cannot play this pattern: {e.Message}");
        }

        LiveNotesOut = pattern;
        NotesOut = null;
        StreamNotesOut = null;
    }

    internal Sound DeclarePoly(int polyphony, Func<Voice, Sound> voice)
    {
        if (polyphony is < 1 or > SigilgraphLimits.MaxPolyphony)
            throw new SigilgraphException($"Poly allows 1 to {SigilgraphLimits.MaxPolyphony} voices; {polyphony} was asked for.");
        ArgumentNullException.ThrowIfNull(voice);
        UsesNoteInput = true;
        return _session.Keys.Play(voice, polyphony);
    }

    internal AudioBuffer DeclareAsset(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new SigilgraphException("Asset needs a name, which becomes its label on the faceplate.");
        if (name.Length > SigilgraphLimits.MaxNameLength)
            throw new SigilgraphException($"Asset name '{name}' is longer than {SigilgraphLimits.MaxNameLength} characters.");

        bool known = _assets.Contains(name, StringComparer.OrdinalIgnoreCase);

        var audio = _session.ResolveAsset(name);
        if (!known)
            _assets.Add(name);
        return audio;
    }

    internal Sound DeclareControl(string name, double @default, double min, double max, Curve curve, bool isSlider)
    {
        string kind = isSlider ? "Slider" : "Knob";
        if (string.IsNullOrWhiteSpace(name))
            throw new SigilgraphException($"{kind} needs a name, which becomes its label on the faceplate.");
        if (name.Length > SigilgraphLimits.MaxNameLength)
            throw new SigilgraphException($"{kind} name '{name}' is longer than {SigilgraphLimits.MaxNameLength} characters.");
        if (!double.IsFinite(@default) || !double.IsFinite(min) || !double.IsFinite(max))
            throw new SigilgraphException($"{kind} '{name}' needs finite numbers for its default, minimum and maximum.");
        if (min >= max)
            throw new SigilgraphException($"{kind} '{name}' has minimum {min} not below maximum {max}.");
        if (@default < min || @default > max)
            throw new SigilgraphException($"{kind} '{name}' has default {@default} outside {min} to {max}.");
        if (curve is Curve.Log && min <= 0)
            throw new SigilgraphException($"{kind} '{name}' uses Curve.Log, so its minimum must be above zero (it is {min}).");
        if (!Enum.IsDefined(curve))
            throw new SigilgraphException($"{kind} '{name}' has an unknown curve.");

        var taper = curve switch
        {
            Curve.Log => ControlTaper.Log,
            Curve.Exp => ControlTaper.Exp,
            _ => ControlTaper.Linear,
        };
        var style = isSlider ? ControlStyle.Slider : ControlStyle.Knob;
        string key = (isSlider ? "s:" : "k:") + name;

        // Asking for the same control twice is fine when it is the same control; the second use shares the first one's sound.
        if (_sounds.TryGetValue(key, out var existing))
        {
            var first = _declared.First(c => c.Style == style && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (first.Min != (float)min || first.Max != (float)max || first.Taper != taper)
                throw new SigilgraphException($"{kind} '{name}' is declared twice with different values; use the same arguments, or a different name.");
            return existing;
        }

        // The control is the page's: it keeps the value you set across edits, like the device's knob does.
        var control = _session.ControlFor(key, name, @default, min, max, taper, style);
        var sound = (Sound)control;
        _declared.Add(control);
        _sounds[key] = sound;
        return sound;
    }

    private static void CheckIndex(int index, int count, string what)
    {
        if ((uint)index >= (uint)count)
            throw new SigilgraphException($"There is no {what} {index}; use 0 to {count - 1}.");
    }
}

/// <summary>
/// The Sigilgraph mode's state for the page: the controls that outlive an edit, the test sources behind <c>In</c>, and the result of the
/// last successful run, which the page shows as the faceplate strip.
/// </summary>
/// <remarks>
/// One per page. It is internal, so the snippets it runs cannot reach it.
/// </remarks>
internal sealed class SigilgraphSession
{
    private readonly Dictionary<string, Control> _controls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Control[] _controlIn;
    private readonly Sound[] _controlInSounds;
    private readonly Slot _audio;
    private SigilgraphSource _source = SigilgraphSource.Saw;

    public SigilgraphSession()
    {
        _controlIn = new Control[SigilgraphLimits.ControlInputs];
        _controlInSounds = new Sound[SigilgraphLimits.ControlInputs];
        for (int i = 0; i < _controlIn.Length; i++)
        {
            // A control input arrives as a signal between -1 and 1; unwired it is zero.
            _controlIn[i] = new Control(0, -1, 1, name: $"Control In {i}") { Polarity = ControlPolarity.Bipolar, Style = ControlStyle.Slider };
            _controlInSounds[i] = _controlIn[i];
        }

        _audio = new Slot(SourceSound(_source));
    }

    /// <summary>One for the page. Its note input and sample lookup are set by the page.</summary>
    public static SigilgraphSession Instance { get; } = new();

    /// <summary>The notes <c>Poly</c> plays. The page sets this to its keyboard.</summary>
    public NoteInput Keys { get; set; } = new();

    /// <summary>Finds the audio of an <c>Asset</c> by name. Unset until the page wires its samples in.</summary>
    public Func<string, AudioBuffer>? AssetResolver { get; set; }

    internal Sound AudioInput => _audio.Sound;

    /// <summary>The controls of the last successful run, in the order the snippet declared them.</summary>
    public IReadOnlyList<Control> Declared { get; private set; } = [];

    /// <summary>The control inputs the last run used, as their controls (so the page can show a slider for each).</summary>
    public IReadOnlyList<Control> ControlInputsUsed { get; private set; } = [];

    /// <summary>The control outputs the last run set, by index.</summary>
    public IReadOnlyList<int> ControlOutputsUsed { get; private set; } = [];

    public IReadOnlyList<string> Assets { get; private set; } = [];

    public bool UsesAudioInput { get; private set; }

    public bool UsesNoteInput { get; private set; }

    /// <summary>True when the last run sends notes with <c>Out.Notes</c>; the page then shows the note strip.</summary>
    public bool SendsNotes { get; private set; }

    /// <summary>The test source behind <c>In.Audio</c>. Changing it swaps the sound with a short crossfade.</summary>
    public SigilgraphSource Source
    {
        get => _source;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown source.");
            _source = value;
            _audio.Swap(SourceSound(value));
        }
    }

    internal Sound ControlInputSound(int index) => _controlInSounds[index];

    internal AudioBuffer ResolveAsset(string name) =>
        (AssetResolver ?? throw new SigilgraphException($"Asset(\"{name}\") is not available in the web REPL yet; load a dropped sample with Samples.Load(\"{name}.wav\") instead."))(name);

    internal Control ControlFor(string key, string name, double @default, double min, double max, ControlTaper taper, ControlStyle style)
    {
        if (_controls.TryGetValue(key, out var kept) && kept.Min == (float)min && kept.Max == (float)max && kept.Taper == taper)
            return kept;

        // New, or the range changed: a new control that starts where the old one was left if that still fits.
        double start = kept is not null && kept.Value >= min && kept.Value <= max ? kept.Value : @default;
        var control = new Control(start, min, max, name: name) { Taper = taper, Style = style };
        _controls[key] = control;
        return control;
    }

    /// <summary>
    /// Runs a snippet's script (which calls <c>Knob</c>, <c>In</c> and the rest) and returns the sound to play. A script that throws
    /// leaves the session as it was.
    /// </summary>
    /// <param name="run">Evaluates the compiled snippet and returns its sound.</param>
    public Sound Build(Func<Sound> run)
    {
        var build = new SigilgraphBuild(this);
        Sound result;
        using (SigilgraphBuild.Enter(build))
            result = run();

        // There is no device to send notes to, so one voice plays them and the page draws them as a strip; the snippet's own sound stays
        // as it was. A pattern of notes goes the same way as a stream, on the page's note clock, as it does on the device's transport.
        var notes = build.StreamNotesOut ?? build.LiveNotesOut?.ToStream();
        if (notes is not null)
            result += Sound.FromOperator(context => new NoteOutTap(notes, DefaultVoice, NoteTrace.Page, Keys.Clock, context), "Notes out");
        else if (build.NotesOut is { } pattern)
            result += pattern.Play(DefaultVoice);
        SendsNotes = notes is not null || build.NotesOut is not null;

        Declared = [.. build.Declared];
        Assets = [.. build.Assets];
        UsesAudioInput = build.UsesAudioInput;
        UsesNoteInput = build.UsesNoteInput;
        ControlInputsUsed = [.. _controlIn.Where((_, i) => build.ControlInputsUsed[i])];
        ControlOutputsUsed = [.. Enumerable.Range(0, SigilgraphLimits.ControlOutputs).Where(i => build.ControlOutputsUsed[i])];
        return result;
    }

    /// <summary>Forgets the last run's faceplate (the page left Sigilgraph mode). The controls keep their values for a return.</summary>
    public void Clear()
    {
        Declared = [];
        Assets = [];
        ControlInputsUsed = [];
        ControlOutputsUsed = [];
        UsesAudioInput = false;
        UsesNoteInput = false;
        SendsNotes = false;
    }

    private static Sound DefaultVoice(Voice v) =>
        Saw(v.Frequency).LowPass(2400).Adsr(attack: 0.01, decay: 0.15, sustain: 0.5, release: 0.2) * v.Velocity * 0.25;

    private static Sound SourceSound(SigilgraphSource source) => source switch
    {
        SigilgraphSource.Noise => Noise() * 0.25,
        SigilgraphSource.Saw => Saw(110) * 0.3,
        _ => Constant(0),
    };
}

/// <summary>What plays into <c>In.Audio</c> in the REPL, in place of whatever a wire would bring to the device.</summary>
internal enum SigilgraphSource
{
    Silence,
    Saw,
    Noise,
}
