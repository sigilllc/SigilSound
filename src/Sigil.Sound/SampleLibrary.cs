namespace Sigil.Sound;

/// <summary>
/// Marks a <see cref="string"/> parameter that takes the name of a sample in the host's sample library, such as the
/// <c>name</c> of <see cref="SampleLibrary.Load"/>. Editors that recognise it offer the library's sample names while you type
/// the argument, so you pick a file instead of remembering how it is spelled.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class SampleNameAttribute : Attribute;

/// <summary>
/// Where a host keeps the sample files its scripts can use: a folder, a browser's storage, an asset database.
/// Scripts never see this; they go through <see cref="SampleLibrary"/>, which only passes on names this source lists.
/// </summary>
/// <remarks>
/// Implement it to give a host its own library. Names are paths relative to the library with forward slashes
/// (<c>kick.wav</c>, <c>drums/snare.wav</c>); they are what scripts type and what completion offers.
/// </remarks>
public interface ISampleSource
{
    /// <summary>
    /// The names of every sample that can be loaded, sorted for display. Called while an editor offers completions, so keep it cheap
    /// (cache the listing and refresh it when the library changes).
    /// </summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>Decodes one sample. <paramref name="name"/> is always one of <see cref="Names"/>.</summary>
    /// <param name="name">A name exactly as listed in <see cref="Names"/>.</param>
    /// <returns>The decoded audio. It is shared and read-only, so the same buffer may be returned for repeated loads.</returns>
    AudioBuffer Load(string name);
}

/// <summary>
/// The samples available to a script, by name. In a script it is the <c>Samples</c> object: <c>Samples.Load("kick.wav")</c>
/// gives an <see cref="AudioBuffer"/> to play with <see cref="Sounds.Sample(AudioBuffer, Param?, PlaybackMode, double, double?)"/>.
/// The browser REPL fills it with the WAV files you drop onto the page, and Audio Workbench's Sound Script with the
/// Asset Manager's sample library, so a script reads the same in both.
/// </summary>
/// <remarks>
/// A script names a sample; it never gives a file path. Only names the host lists can be loaded, so a script cannot reach any
/// other file on the machine.
/// </remarks>
public sealed class SampleLibrary
{
    private const int SuggestionLimit = 8;

    private readonly ISampleSource _source;

    /// <summary>Creates a library that reads from <paramref name="source"/>.</summary>
    /// <param name="source">The host's sample store.</param>
    public SampleLibrary(ISampleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>The names of the samples that can be loaded right now, for example <c>kick.wav</c> or <c>drums/snare.wav</c>.</summary>
    public IReadOnlyList<string> Names => _source.Names;

    /// <summary>Whether a sample with this name exists. Case is ignored if no name matches exactly.</summary>
    /// <param name="name">The sample's name as listed in <see cref="Names"/>.</param>
    public bool Contains(string name) => Find(name) is not null;

    /// <summary>
    /// Loads a sample from the library, ready to play with <c>Sample(...)</c>. While typing the name, the editor lists the samples
    /// that are available. Loading happens when the script is compiled, not while it plays, and the same sample can be loaded
    /// any number of times.
    /// </summary>
    /// <param name="name">The sample's name, such as <c>"kick.wav"</c> or <c>"drums/snare.wav"</c>. Matching ignores case when there is no exact match.</param>
    /// <returns>The decoded audio at the file's own sample rate; playback converts it to the output rate.</returns>
    /// <exception cref="KeyNotFoundException">There is no sample with that name. The message lists names that are available.</exception>
    /// <example>
    /// <code>
    /// // The REPL ships with pluck.wav; drop your own .wav files on the page to add more.
    /// var pluck = Samples.Load("pluck.wav");
    /// Sample(pluck, speed: 1)
    /// </code>
    /// </example>
    public AudioBuffer Load([SampleName] string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string? found = Find(name);
        if (found is null)
            throw new KeyNotFoundException(NotFoundMessage(name));
        return _source.Load(found);
    }

    private string? Find(string name)
    {
        string wanted = name.Replace('\\', '/');
        var names = _source.Names;
        foreach (string candidate in names)
        {
            if (string.Equals(candidate, wanted, StringComparison.Ordinal))
                return candidate;
        }
        foreach (string candidate in names)
        {
            if (string.Equals(candidate, wanted, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;
    }

    private string NotFoundMessage(string name)
    {
        var names = _source.Names;
        if (names.Count == 0)
            return $"There is no sample named \"{name}\": the sample library is empty.";

        // Names that contain what was typed are the likely ones; otherwise show the start of the list.
        var close = names.Where(n => n.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        var shown = (close.Count > 0 ? close : names).Take(SuggestionLimit).ToList();
        int more = (close.Count > 0 ? close.Count : names.Count) - shown.Count;
        string list = string.Join(", ", shown) + (more > 0 ? $" and {more} more" : "");
        return $"There is no sample named \"{name}\". {(close.Count > 0 ? "Similar" : "Available")}: {list}.";
    }
}
