using Sigil.Sound;
using Sigil.Sound.Repl;

namespace ReplHost;

/// <summary>
/// What the REPL page hands to the snippets you type, on top of the library: a live keyboard and the samples you dropped on the page.
/// Snippets reach them as plain names (<c>Keys</c>, <c>Samples</c>) through <c>ReplProfile</c>; nothing in the compiler or in
/// Sigil.Sound knows this type exists, which is the same arrangement any other host of the library would use.
/// </summary>
/// <remarks>
/// Lives in its own namespace because the base policy blocks everything under <c>Sigil.Sound.Repl</c>, the REPL's own
/// implementation. Keep it small: every public member here is something a pasted snippet can call.
/// </remarks>
public static class ReplApi
{
    /// <summary>
    /// The notes played on the keyboard under the editor. <c>Keys.Play(v =&gt; ...)</c> turns them into sound.
    /// One instance lives for the whole page, so keys held while you edit a snippet keep sounding in the new version.
    /// </summary>
    public static NoteInput Keys { get; } = new();

    /// <summary>
    /// The WAV files you dropped onto the page. <c>Samples.Load("kick.wav")</c> gives audio to play with <c>Sample(...)</c>, and the
    /// editor lists the available names as you type the quotes. Files are kept in this browser between visits.
    /// </summary>
    public static SampleLibrary Samples { get; } = new(ReplSamples.Instance.Source);
}
