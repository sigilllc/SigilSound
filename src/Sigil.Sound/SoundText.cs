using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Sigil.Sound;

/// <summary>
/// The text <see cref="Sound.ToString"/> returns, kept as its parts and put together only when someone asks for it.
/// </summary>
/// <remarks>
/// <para>
/// Every method that builds a sound writes its description as an interpolated string, <c>$"{this}.Gain({gain})"</c>. Written out at
/// once, each description contains the whole text of the sound it wraps, so a sound that is used twice doubles its text and one used
/// by itself twenty times is millions of characters: <c>s = s + s</c> in a loop is enough to run a host out of memory. As a handler
/// the string is not built when the sound is: the parts are kept, and the pieces that are sounds are described, within a budget
/// (see <see cref="Sound.ToString"/>), only when the text is read.
/// </para>
/// <para>
/// Numbers are written with the invariant culture, so a description reads the same on every machine.
/// </para>
/// </remarks>
[InterpolatedStringHandler]
internal readonly struct SoundText
{
    private readonly object? _content;

    private SoundText(string text) => _content = text;

    /// <summary>Called by the compiler for an interpolated string.</summary>
    public SoundText(int literalLength, int formattedCount) => _content = new List<Part>(formattedCount * 2 + 1);

    public static implicit operator SoundText(string text) => new(text);

    public void AppendLiteral(string value) => Parts.Add(new Part(value, null, null));

    public void AppendFormatted<T>(T value) => Parts.Add(new Part(null, value, null));

    public void AppendFormatted<T>(T value, string? format) => Parts.Add(new Part(null, value, format));

    private List<Part> Parts => (List<Part>)_content!;

    public override string ToString()
    {
        if (_content is string text)
            return text;
        if (_content is not List<Part> parts)
            return "";

        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Literal is not null)
                builder.Append(part.Literal);
            else if (part.Value is IFormattable formattable)
                builder.Append(formattable.ToString(part.Format, CultureInfo.InvariantCulture));
            else
                builder.Append(part.Value);
        }

        return builder.ToString();
    }

    private readonly record struct Part(string? Literal, object? Value, string? Format);
}
