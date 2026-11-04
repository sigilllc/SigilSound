namespace Sigil.Sound.Repl;

/// <summary>
/// A sample library that is a folder of .wav files, searched in subfolders too. Names are the paths below the folder with forward
/// slashes (<c>kick.wav</c>, <c>drums/snare.wav</c>). The browser REPL points it at its virtual folder, and a desktop host at the
/// user's sample directory, so both list and load samples the same way.
/// </summary>
/// <remarks>
/// <para>
/// The listing is cached and rescanned when <see cref="Refresh"/> is called or when it is older than <see cref="RescanInterval"/>,
/// which keeps completion cheap on a large library while still noticing files that were added by something else.
/// Decoded audio is kept until the file changes or disappears, because a script is compiled again on every edit.
/// </para>
/// <para>Only files that are listed can be loaded, so a name can never point outside the folder.</para>
/// </remarks>
public sealed class FolderSampleSource : ISampleSource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Decoded> _decoded = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _names;
    private DateTime _scannedAt;

    private sealed record Decoded(long Ticks, long Length, AudioBuffer Audio);

    /// <param name="root">The folder that holds the samples. It does not need to exist yet.</param>
    /// <param name="maxFileBytes">The largest file that will be decoded; bigger ones are refused with an explanation.</param>
    public FolderSampleSource(string root, long maxFileBytes = 64L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        MaxFileBytes = maxFileBytes;
    }

    /// <summary>The folder the samples live in, as a full path.</summary>
    public string Root { get; }

    /// <summary>The largest file, in bytes, that <see cref="Load"/> will decode.</summary>
    public long MaxFileBytes { get; }

    /// <summary>How long a listing is trusted before the folder is looked at again.</summary>
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public IReadOnlyList<string> Names
    {
        get
        {
            lock (_gate)
            {
                if (_names is null || DateTime.UtcNow - _scannedAt > RescanInterval)
                    Scan();
                return _names!;
            }
        }
    }

    /// <summary>Forgets the cached listing, so the next request looks at the folder. Call it after adding or removing files.</summary>
    public void Refresh()
    {
        lock (_gate)
            _names = null;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">The file is larger than <see cref="MaxFileBytes"/> or is not a supported WAV file.</exception>
    public AudioBuffer Load(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string path = Resolve(name);

        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"The sample \"{name}\" is no longer in the library.", name);
        if (info.Length > MaxFileBytes)
            throw new InvalidDataException($"The sample \"{name}\" is {info.Length / (1024 * 1024)} MB; the limit is {MaxFileBytes / (1024 * 1024)} MB.");

        lock (_gate)
        {
            if (_decoded.TryGetValue(name, out var cached) && cached.Ticks == info.LastWriteTimeUtc.Ticks && cached.Length == info.Length)
                return cached.Audio;
        }

        var audio = AudioBuffer.FromWav(path);
        lock (_gate)
            _decoded[name] = new Decoded(info.LastWriteTimeUtc.Ticks, info.Length, audio);
        return audio;
    }

    // A name is only used after it matches the listing, which cannot contain a path outside the root; the check here is the second lock on that door.
    private string Resolve(string name)
    {
        if (!Names.Contains(name, StringComparer.Ordinal))
            throw new KeyNotFoundException($"There is no sample named \"{name}\".");

        string path = Path.GetFullPath(Path.Combine(Root, name));
        string prefix = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            throw new KeyNotFoundException($"There is no sample named \"{name}\".");
        return path;
    }

    private void Scan()
    {
        var names = new List<string>();
        if (Directory.Exists(Root))
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    continue;

                string name = Path.GetRelativePath(Root, file).Replace('\\', '/');
                // A name goes between quotes in a script, so one that could not be written there is left out.
                if (name.Any(c => c is '"' or '\\' || char.IsControl(c)))
                    continue;
                names.Add(name);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        _names = names;
        _scannedAt = DateTime.UtcNow;

        // Drop decoded audio for files that are gone.
        var present = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (string gone in _decoded.Keys.Where(k => !present.Contains(k)).ToList())
            _decoded.Remove(gone);
    }
}
