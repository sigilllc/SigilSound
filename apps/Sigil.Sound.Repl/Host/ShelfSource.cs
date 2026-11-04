using Sigil.Sound.Wav;

namespace Sigil.Sound.Repl;

/// <summary>
/// What snippets see as the page's sample library: the user's files, then the samples that ship with the page. When both have a
/// name, the user's file is the one loaded, so dropping your own <c>pluck.wav</c> replaces the built-in one for your snippets.
/// </summary>
internal sealed class ShelfSource : ISampleSource
{
    private readonly FolderSampleSource _folder;
    private readonly IReadOnlyDictionary<string, byte[]> _builtIn;
    private readonly Dictionary<string, AudioBuffer> _decoded = new(StringComparer.Ordinal);

    public ShelfSource(FolderSampleSource folder, IReadOnlyDictionary<string, byte[]> builtIn)
    {
        _folder = folder;
        _builtIn = builtIn;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names
    {
        get
        {
            var own = _folder.Names;
            if (_builtIn.Count == 0)
                return own;

            // A built-in name hidden by a user file differing only in case would otherwise be listed twice.
            var known = new HashSet<string>(own, StringComparer.OrdinalIgnoreCase);
            var all = new List<string>(own);
            all.AddRange(_builtIn.Keys.Where(known.Add));
            all.Sort(StringComparer.OrdinalIgnoreCase);
            return all;
        }
    }

    /// <inheritdoc />
    public AudioBuffer Load(string name)
    {
        if (_folder.Names.Contains(name, StringComparer.Ordinal))
            return _folder.Load(name);

        if (!_builtIn.TryGetValue(name, out var bytes))
            throw new KeyNotFoundException($"There is no sample named \"{name}\".");

        lock (_decoded)
        {
            if (!_decoded.TryGetValue(name, out var audio))
                _decoded[name] = audio = WavReader.Read(bytes);
            return audio;
        }
    }
}
