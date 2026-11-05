using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.JSInterop;

namespace Sigil.Sound.Repl;

/// <summary>
/// Times an example on this page's own runtime, so a performance change can be measured where it matters: in the browser, on the
/// .NET interpreter, on the page's only thread. The page never calls it; a script does, e.g. from headless Chrome:
/// <c>DotNet.invokeMethodAsync("Sigil.Sound.Repl", "Bench", "Pattern groove", 10)</c>.
/// </summary>
/// <remarks>
/// Renders the way <see cref="ReplEngine"/> does (chunks of 50 ms), so a chunk that takes longer than 50 ms is an audible underrun.
/// Lives in the blocked <c>Sigil.Sound.Repl</c> namespace, so pasted snippets cannot reach it.
/// </remarks>
public static class ReplBench
{
    private const double ChunkSeconds = 0.05;

    /// <summary>Compiles the named example, renders <paramref name="seconds"/> of it, and returns a JSON summary of the chunk times.</summary>
    [JSInvokable("Bench")]
    public static string Run(string exampleName, double seconds)
    {
        var example = ReplExamples.All.FirstOrDefault(e => e.Name == exampleName);
        if (example is null)
            return $"{{\"error\":\"no example named {exampleName}\"}}";

        var inv = CultureInfo.InvariantCulture;
        var compileTimer = Stopwatch.StartNew();
        var result = SnippetCompiler.Compile(example.Code, ReplProfile.Instance);
        if (!result.Success)
            return "{\"error\":\"does not compile\"}";
        var sound = result.Run();
        double compileMs = compileTimer.Elapsed.TotalMilliseconds;

        int sampleRate = RenderContext.DefaultSampleRate;
        using var context = new SoundContext(sampleRate, fadeSeconds: 0.03);
        context.Play(sound);

        int frames = (int)(ChunkSeconds * sampleRate);
        var buffer = new StereoFrame[frames];
        var times = new List<double>();
        long total = (long)(seconds * sampleRate);
        for (long done = 0; done < total; done += frames)
        {
            ExecutionGuard.Reset(frames);
            long began = Stopwatch.GetTimestamp();
            context.Render(buffer);
            times.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds);
        }

        // The first second warms the interpreter's caches (and the first notes' voices); report it apart from the rest.
        int skip = Math.Min(times.Count / 4, (int)(1.0 / ChunkSeconds));
        var steady = times.Skip(skip).OrderBy(t => t).ToList();
        double Pct(double p) => steady[Math.Min(steady.Count - 1, (int)(p * steady.Count))];
        double budget = ChunkSeconds * 1000;

        var sb = new StringBuilder("{");
        sb.Append(inv, $"\"example\":\"{exampleName}\",\"compileMs\":{compileMs:0},\"chunkBudgetMs\":{budget:0.#},\"chunks\":{times.Count},");
        sb.Append(inv, $"\"meanMs\":{steady.Average():0.00},\"p50Ms\":{Pct(.5):0.00},\"p99Ms\":{Pct(.99):0.00},\"maxMs\":{steady[^1]:0.00},");
        sb.Append(inv, $"\"meanLoad\":{steady.Average() / budget:0.000},\"overBudget\":{steady.Count(t => t > budget)},\"firstChunksMs\":[");
        sb.Append(string.Join(",", times.Take(skip).Select(t => t.ToString("0.0", inv))));
        sb.Append("]}");
        return sb.ToString();
    }
}
