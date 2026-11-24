using Sigil.Sound;

namespace LiveScript;

/// <summary>What this host gives a script, through <c>using static LiveScript.Host;</c>: the computer keyboard as a note input.</summary>
public static class Host
{
    /// <summary>
    /// The notes played on the computer keyboard: A S D F G H J K are C to C, W E T Y U the black keys, Z and X move the octave.
    /// It lives as long as the program, so a note held across a save keeps sounding in the new script.
    /// </summary>
    public static NoteInput Keys { get; } = new();
}
