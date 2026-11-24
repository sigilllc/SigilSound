using LiveScript;
using NAudio.Wave;
using Sigil.Sound;
using Sigil.Sound.Repl;

// A live-coding host in one file: play a Sigil.Sound script, and swap it in every time the file is saved.
//
//   dotnet run --project samples/LiveScript [path\to\script.cs]
//
// With no path it plays script.cs in this project's folder. Everything Sigil.Sound needs from a host is here: a SoundContext as
// the output (every Play is a hot swap), the snippet compiler with a profile that adds this host's API, and a NoteInput.

var path = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "script.cs"));
if (!File.Exists(path))
{
    Console.WriteLine($"No script at {path}.");
    return 1;
}

var profile = new SnippetProfile(
    extraUsings: ["using static LiveScript.Host;"],
    extraAssemblies: [typeof(Host).Assembly],
    extraAllowedNamespaces: ["LiveScript"]);

using var output = new SoundContext(sampleRate: 48_000, fadeSeconds: 0.05);

void Load()
{
    string code;
    try
    {
        // An editor may still hold the file for a moment after it saves.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        code = new StreamReader(stream).ReadToEnd();
    }
    catch (IOException)
    {
        return;
    }

    var result = SnippetCompiler.Compile(code, profile);
    if (!result.Success)
    {
        foreach (var error in result.Diagnostics.Where(d => d.IsError))
            Console.WriteLine($"  {error.Message}");
        Console.WriteLine("not changed: the script has errors, so the last good version keeps playing");
        return;
    }

    try
    {
        ExecutionGuard.Reset();
        output.Play(result.Run());
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} playing {Path.GetFileName(path)}");
    }
    catch (Exception e)
    {
        Console.WriteLine($"not changed: {e.Message}");
    }
}

Load();

// One save arrives as several change events; load once they have stopped for 150 ms.
using var debounce = new Timer(_ => Load());
using var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
{
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
    EnableRaisingEvents = true,
};
watcher.Changed += (_, _) => debounce.Change(150, Timeout.Infinite);
watcher.Created += (_, _) => debounce.Change(150, Timeout.Infinite);
watcher.Renamed += (_, _) => debounce.Change(150, Timeout.Infinite);

using var device = new WaveOutEvent { DesiredLatency = 80 };
device.Init(new SoundContextWaveProvider(output));
device.Play();

Console.WriteLine($"Editing {path}");
Console.WriteLine("A S D F G H J K and W E T Y U play notes, Z and X change the octave, Esc quits.");

// Without a console keyboard (input redirected, or run from an IDE without one) the script still plays and still reloads.
if (Console.IsInputRedirected)
{
    Console.WriteLine("No console keyboard here: the script plays and reloads on save. Ctrl+C quits.");
    using var quit = new ManualResetEventSlim();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        quit.Set();
    };
    quit.Wait();
    output.Stop(0.2);
    Thread.Sleep(250);
    return 0;
}

// A console cannot tell when a key is let go, so each key press is a note of 0.4 seconds.
const string whiteKeys = "asdfghjk";
const string blackKeys = "we.tyu";
int[] whiteSteps = [0, 2, 4, 5, 7, 9, 11, 12];
int[] blackSteps = [1, 3, -1, 6, 8, 10];
int octave = 4;
while (true)
{
    var key = Console.ReadKey(intercept: true);
    if (key.Key == ConsoleKey.Escape)
        break;

    char c = char.ToLowerInvariant(key.KeyChar);
    if (c == 'z')
    {
        octave = Math.Max(1, octave - 1);
        continue;
    }
    if (c == 'x')
    {
        octave = Math.Min(7, octave + 1);
        continue;
    }

    int white = whiteKeys.IndexOf(c), black = blackKeys.IndexOf(c);
    int step = white >= 0 ? whiteSteps[white] : black >= 0 ? blackSteps[black] : -1;
    if (step < 0)
        continue;

    int pitch = 12 * (octave + 1) + step;
    Host.Keys.NoteOn(pitch);
    _ = Task.Delay(400).ContinueWith(_ => Host.Keys.NoteOff(pitch));
}

output.Stop(0.2);
Thread.Sleep(250);
return 0;
