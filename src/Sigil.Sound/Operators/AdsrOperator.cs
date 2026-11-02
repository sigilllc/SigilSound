namespace Sigil.Sound.Operators;

/// <summary>
/// Applies a linear attack-decay-sustain-release envelope to its input.
/// The release stage begins at a fixed time (the "hold"), and the release ramps down from
/// whatever level the envelope reached, so an early release never jumps.
/// </summary>
public sealed class AdsrOperator : IOperator
{
    private readonly IOperator _input;
    private readonly double _attack;
    private readonly double _decay;
    private readonly float _sustain;
    private readonly double _release;
    private readonly double _releaseAt;
    private readonly double _samplePeriod;

    private double _time;
    private float _level;
    private float _releaseStartLevel = -1;

    public AdsrOperator(IOperator input, double attack, double decay, float sustain, double release,
        double releaseAt, RenderContext context)
    {
        _input = input;
        _attack = attack;
        _decay = decay;
        _sustain = sustain;
        _release = release;
        _releaseAt = Math.Max(0, releaseAt);
        _samplePeriod = context.SamplePeriod;
    }

    /// <summary>True once the release stage has reached zero.</summary>
    public bool IsFinished => _time >= _releaseAt + _release && _releaseStartLevel >= 0;

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        for (int i = 0; i < block.Length; i++)
        {
            block[i] *= NextLevel();
            _time += _samplePeriod;
        }
    }

    private float NextLevel()
    {
        if (_time >= _releaseAt)
        {
            if (_releaseStartLevel < 0)
                _releaseStartLevel = _level;

            double intoRelease = _time - _releaseAt;
            _level = intoRelease >= _release || _release <= 0
                ? 0f
                : _releaseStartLevel * (float)(1.0 - intoRelease / _release);
            return _level;
        }

        if (_time < _attack)
            _level = (float)(_time / _attack);
        else if (_time < _attack + _decay)
            _level = 1.0f - (1.0f - _sustain) * (float)((_time - _attack) / _decay);
        else
            _level = _sustain;

        return _level;
    }
}
