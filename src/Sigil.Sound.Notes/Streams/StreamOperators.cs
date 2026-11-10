namespace Sigil.Sound;

/// <summary>Plays the notes of a stream with a voice pool: starts on a start, releases on the matching end.</summary>
internal sealed class StreamPlayOperator : IOperator, IResettable, IDisposable
{
    private readonly NoteStreamRunner _runner;
    private readonly VoicePool _voices;
    private long _position;

    public StreamPlayOperator(NoteStream stream, Func<Voice, Sound> factory, int polyphony, RenderContext context)
    {
        _voices = new VoicePool(factory, polyphony, context, stealReleasedFirst: true);
        _runner = stream.Compile(context.TimeClock, context.SampleRate);
    }

    public void Reset()
    {
        _voices.Reset();
        _runner.Reset();
        _position = 0;
    }

    public void Dispose()
    {
        _runner.Dispose();
        _voices.Dispose();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        foreach (var e in _runner.Process(block.Length))
        {
            if (e.Kind == NoteEventKind.On)
                _voices.Start(e.Frequency, e.Pitch, e.Velocity, double.PositiveInfinity, _position, id: e.Id);
            else
                _voices.ReleaseId(e.Id, _position);
        }

        block.Clear();
        _voices.Render(block, _position);
        _position += block.Length;
    }
}

/// <summary>A signal that is 1 while the stream has a note sounding.</summary>
internal sealed class StreamGateOperator : IOperator, IResettable, IDisposable
{
    private readonly NoteStreamRunner _runner;

    public StreamGateOperator(NoteStream stream, RenderContext context) => _runner = stream.Compile(context.TimeClock, context.SampleRate);

    public void Reset() => _runner.Reset();

    public void Dispose() => _runner.Dispose();

    public void RenderBlock(Span<StereoFrame> block)
    {
        // The state at the end of the block, so a note that started in it opens the gate from the block's first frame.
        _ = _runner.Process(block.Length);
        float level = _runner.Sounding > 0 ? 1f : 0f;
        block.Fill(new StereoFrame(level, level));
    }
}
