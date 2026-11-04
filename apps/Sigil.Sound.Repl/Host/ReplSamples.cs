using System.Text;
using Sigil.Sound.Wav;

namespace Sigil.Sound.Repl;

/// <summary>One sample on the page's shelf: what the list shows.</summary>
/// <param name="Name">The name a snippet uses, such as <c>kick.wav</c>.</param>
/// <param name="Bytes">The size of the file.</param>
/// <param name="Seconds">The length of the audio, or 0 when the file could not be read.</param>
/// <param name="BuiltIn">True for a sample that ships with the page: it cannot be removed, only shadowed by a file of the same name.</param>
public sealed record SampleEntry(string Name, long Bytes, double Seconds, bool BuiltIn = false);

/// <summary>The outcome of adding a file: whether it was kept, under what name, and a sentence for the status bar.</summary>
public sealed record SampleAddResult(bool Ok, string Name, string Message);

/// <summary>
/// The WAV files the page holds for snippets: a folder in the browser's virtual file system (<c>/userfs/samples</c>) that
/// <c>Samples.Load("kick.wav")</c> reads from. Files arrive by drag and drop or the file picker, and the page keeps a copy in the
/// browser's storage so they are still there next visit.
/// </summary>
/// <remarks>
/// Everything lives in memory in the browser, so the library is capped. A file is checked from its header before it is kept:
/// refusing a stereo 5.1 recording at the drop is kinder than failing when the snippet runs.
/// </remarks>
internal sealed class ReplSamples
{
    /// <summary>Where the page's samples live in the browser's virtual file system.</summary>
    public const string DefaultRoot = "/userfs/samples";

    /// <summary>The largest single file. Decoded audio is held as 32-bit stereo, up to four times the size of a 16-bit mono file.</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>The most all samples may take together.</summary>
    public const long MaxTotalBytes = 60L * 1024 * 1024;

    /// <summary>The most files at once.</summary>
    public const int MaxFiles = 200;

    private const int MaxNameLength = 80;

    private readonly Dictionary<string, (long Bytes, double Seconds)> _lengths = new(StringComparer.Ordinal);

    private readonly FolderSampleSource _folder;
    private readonly IReadOnlyDictionary<string, byte[]> _builtIn;

    /// <summary>The page's one library, shared by every snippet: the user's files plus the samples that ship with the page.</summary>
    public static ReplSamples Instance { get; } = new(DefaultRoot, BuiltInSamples.FromAssembly());

    /// <param name="root">The folder the user's samples live in.</param>
    /// <param name="builtIn">Samples that ship with the page, by name, as the bytes of a .wav file. They are listed after the user's own
    /// and a user file with the same name wins.</param>
    public ReplSamples(string root, IReadOnlyDictionary<string, byte[]>? builtIn = null)
    {
        _folder = new FolderSampleSource(root, MaxFileBytes);
        _builtIn = builtIn ?? new Dictionary<string, byte[]>();
        Source = new ShelfSource(_folder, _builtIn);
    }

    /// <summary>What snippets and completion read from.</summary>
    public ISampleSource Source { get; }

    /// <summary>The samples on the shelf, with sizes and lengths, in listing order. Built-in ones are marked.</summary>
    public IReadOnlyList<SampleEntry> Entries()
    {
        var entries = new List<SampleEntry>();
        foreach (string name in Source.Names)
        {
            if (_folder.Names.Contains(name, StringComparer.Ordinal))
            {
                string path = Path.Combine(_folder.Root, name);
                long bytes = new FileInfo(path).Length;
                entries.Add(new SampleEntry(name, bytes, SecondsOf(name, path, bytes)));
            }
            else
            {
                var data = _builtIn[name];
                entries.Add(new SampleEntry(name, data.Length, SecondsOf(data), BuiltIn: true));
            }
        }
        return entries;
    }

    /// <summary>
    /// Checks a dropped file and keeps it. A file with the same name is replaced, so dropping a new take over the old one just works.
    /// </summary>
    /// <param name="fileName">The name the file had when it was dropped; folders in it are ignored and odd characters replaced.</param>
    /// <param name="bytes">The file's contents.</param>
    public SampleAddResult Add(string fileName, byte[] bytes)
    {
        string? name = CleanName(fileName);
        if (name is null)
            return new SampleAddResult(false, fileName, $"\"{fileName}\" was not added: only .wav files can be used as samples.");

        WavInfo info;
        try
        {
            info = WavReader.ReadInfo(bytes);
        }
        catch (InvalidDataException e)
        {
            return new SampleAddResult(false, name, $"\"{name}\" was not added: {e.Message}");
        }

        if (bytes.Length > MaxFileBytes)
            return new SampleAddResult(false, name, $"\"{name}\" was not added: it is {bytes.Length / (1024 * 1024)} MB and the limit is {MaxFileBytes / (1024 * 1024)} MB.");

        string path = Path.Combine(_folder.Root, name);
        bool replacing = File.Exists(path);
        long existing = replacing ? new FileInfo(path).Length : 0;
        var (count, total) = Totals();
        if (!replacing && count >= MaxFiles)
            return new SampleAddResult(false, name, $"\"{name}\" was not added: the page holds at most {MaxFiles} samples. Remove some first.");
        if (total - existing + bytes.Length > MaxTotalBytes)
            return new SampleAddResult(false, name, $"\"{name}\" was not added: samples may take {MaxTotalBytes / (1024 * 1024)} MB together. Remove some first.");

        Directory.CreateDirectory(_folder.Root);
        File.WriteAllBytes(path, bytes);
        _lengths.Remove(name);
        _folder.Refresh();

        string shape = $"{info.Duration:0.##} s, {(info.Channels == 1 ? "mono" : "stereo")}, {info.SampleRate / 1000.0:0.#} kHz";
        return new SampleAddResult(true, name, $"{(replacing ? "Replaced" : "Added")} \"{name}\" ({shape}). Use Samples.Load(\"{name}\").");
    }

    /// <summary>
    /// Puts back a sample saved by an earlier visit. It was checked when it was first added, so only the name and the size caps are
    /// looked at; anything that no longer fits is skipped.
    /// </summary>
    /// <returns>Whether the file was restored.</returns>
    public bool Restore(string fileName, byte[] bytes)
    {
        string? name = CleanName(fileName);
        var (count, total) = Totals();
        if (name is null || bytes.Length > MaxFileBytes || count >= MaxFiles || total + bytes.Length > MaxTotalBytes)
            return false;

        Directory.CreateDirectory(_folder.Root);
        File.WriteAllBytes(Path.Combine(_folder.Root, name), bytes);
        _lengths.Remove(name);
        _folder.Refresh();
        return true;
    }

    /// <summary>
    /// Deletes one of the user's samples. Returns false when there was none by that name, which includes a built-in sample:
    /// those ship with the page. If a user file was shadowing a built-in one, the built-in one is heard again.
    /// </summary>
    public bool Remove(string name)
    {
        if (!_folder.Names.Contains(name, StringComparer.Ordinal))
            return false;
        File.Delete(Path.Combine(_folder.Root, name));
        _lengths.Remove(name);
        _folder.Refresh();
        return true;
    }

    /// <summary>
    /// The name a dropped file is kept under: its file name without any folders, with characters that cannot be typed safely in a
    /// snippet replaced by underscores. Null when it is not a .wav file.
    /// </summary>
    public static string? CleanName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        string leaf = fileName.Replace('\\', '/');
        leaf = leaf[(leaf.LastIndexOf('/') + 1)..].Trim();
        if (!leaf.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || leaf.Length <= ".wav".Length)
            return null;

        var cleaned = new StringBuilder(leaf.Length);
        foreach (char c in leaf)
            cleaned.Append(char.IsLetterOrDigit(c) || c is ' ' or '.' or '_' or '-' or '(' or ')' or '+' or '#' ? c : '_');

        string result = cleaned.ToString().TrimStart('.');
        if (result.Length > MaxNameLength)
            result = result[..(MaxNameLength - ".wav".Length)].TrimEnd() + ".wav";

        // Always spelled ".wav" in lower case, so "KICK.WAV" and "kick.wav" are one name.
        return result.Length > ".wav".Length ? result[..^".wav".Length] + ".wav" : null;
    }

    // The caps are about what the user added; the samples that ship with the page do not count against them.
    private (int Count, long Bytes) Totals()
    {
        long bytes = 0;
        var names = _folder.Names;
        foreach (string name in names)
            bytes += new FileInfo(Path.Combine(_folder.Root, name)).Length;
        return (names.Count, bytes);
    }

    private static double SecondsOf(byte[] data)
    {
        try
        {
            return WavReader.ReadInfo(data).Duration;
        }
        catch (InvalidDataException)
        {
            return 0;
        }
    }

    // The length comes from the header, so listing a hundred samples does not decode any of them. Remembered per name and
    // size, because the list is rebuilt on every change and a file only needs reading once.
    private double SecondsOf(string name, string path, long bytes)
    {
        if (_lengths.TryGetValue(name, out var known) && known.Bytes == bytes)
            return known.Seconds;

        double seconds;
        try
        {
            seconds = WavReader.ReadInfo(File.ReadAllBytes(path)).Duration;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            seconds = 0;
        }
        _lengths[name] = (bytes, seconds);
        return seconds;
    }
}
