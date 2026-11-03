namespace Sigil.Sound.Operators;

/// <summary>
/// Applies a linear attack-decay-sustain-release envelope to its input.
/// The release stage begins at a fixed time (the "hold"), and the release ramps down from
/// whatever level the envelope had reached, so an early release never jumps.
/// Without an explicit hold, a note voice releases when its note ends and anything else sustains.
/// </summary>
internal sealed class AdsrOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly double _attack;
    private readonly double _decay;
    private readonly float _sustain;
    private readonly double _release;
    private readonly double? _hold;
    private readonly VoiceControl? _voice;
    private readonly double _samplePeriod;

    private double _releaseAt = double.PositiveInfinity;
    private double _time;
    private float _releaseStartLevel = -1;

    public AdsrOperator(IOperator input, double attack, double decay, float sustain, double release,
        double? hold, RenderContext context)
    {
        _input = input;
        _attack = attack;
        _decay = decay;
        _sustain = sustain;
        _release = release;
        _hold = hold;
        _voice = context.Voice;
        _samplePeriod = context.SamplePeriod;
    }

    public void Reset()
    {
        _time = 0;
        _releaseStartLevel = -1;
        _input.ResetState();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        // An explicit hold wins; otherwise the note decides (read each block, so a retriggered voice follows its new note).
        _releaseAt = Math.Max(0, _hold ?? _voice?.HoldSeconds ?? double.PositiveInfinity);

        _input.RenderBlock(block);
        for (int i = 0; i < block.Length; i++)
        {
            block[i] *= NextLevel();
            _time += _samplePeriod;
        }
    }

    // The level before any release: rise, fall to the sustain, hold it.
    private float Envelope(double time)
    {
        if (time < _attack)
            return (float)(time / _attack);
        if (time < _attack + _decay)
            return 1.0f - (1.0f - _sustain) * (float)((time - _attack) / _decay);
        return _sustain;
    }

    private float NextLevel()
    {
        if (_time < _releaseAt)
            return Envelope(_time);

        // Ramp down from what the envelope would have been at the moment of release. Reading it from the previous
        // frame instead would start the release from silence when the release begins on the very first frame.
        if (_releaseStartLevel < 0)
            _releaseStartLevel = Envelope(_releaseAt);

        double intoRelease = _time - _releaseAt;
        return intoRelease >= _release || _release <= 0
            ? 0f
            : _releaseStartLevel * (float)(1.0 - intoRelease / _release);
    }
}
