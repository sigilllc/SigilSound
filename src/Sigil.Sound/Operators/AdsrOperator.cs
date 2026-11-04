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
    private readonly ParamSource _attack;
    private readonly ParamSource _decay;
    private readonly ParamSource _sustain;
    private readonly ParamSource _release;
    private readonly double? _hold;
    private readonly VoiceControl? _voice;
    private readonly double _samplePeriod;

    private double _releaseAt = double.PositiveInfinity;
    private double _time;
    private float _releaseStartLevel = -1;

    public AdsrOperator(IOperator input, ParamSource attack, ParamSource decay, ParamSource sustain, ParamSource release,
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
        _attack.Reset();
        _decay.Reset();
        _sustain.Reset();
        _release.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        // An explicit hold wins; otherwise the note decides (read each block, so a retriggered voice follows its new note).
        _releaseAt = Math.Max(0, _hold ?? _voice?.HoldSeconds ?? double.PositiveInfinity);

        _input.RenderBlock(block);

        double time = _time;
        double period = _samplePeriod;
        double releaseAt = _releaseAt;

        if (_attack.IsConstant && _decay.IsConstant && _sustain.IsConstant && _release.IsConstant)
        {
            float attack = _attack.Next(1)[0];
            float decay = _decay.Next(1)[0];
            float sustain = _sustain.Next(1)[0];
            float release = _release.Next(1)[0];
            double attackAndDecay = attack + decay;

            for (int i = 0; i < block.Length; i++)
            {
                float level;
                if (time < releaseAt)
                {
                    if (attack > 0 && time < attack)
                        level = (float)(time / attack);
                    else if (decay > 0 && time < attackAndDecay)
                        level = 1.0f - (1.0f - sustain) * (float)((time - attack) / decay);
                    else
                        level = sustain;
                }
                else
                {
                    if (_releaseStartLevel < 0)
                        _releaseStartLevel = Envelope(releaseAt, attack, decay, sustain);

                    double intoRelease = time - releaseAt;
                    level = intoRelease >= release || release <= 0
                        ? 0f
                        : _releaseStartLevel * (float)(1.0 - intoRelease / release);
                }

                var frame = block[i];
                block[i] = new StereoFrame(frame.Left * level, frame.Right * level);
                time += period;
            }
        }
        else
        {
            var attackSpan = _attack.Next(block.Length);
            var decaySpan = _decay.Next(block.Length);
            var sustainSpan = _sustain.Next(block.Length);
            var releaseSpan = _release.Next(block.Length);

            for (int i = 0; i < block.Length; i++)
            {
                float attack = attackSpan[i];
                float decay = decaySpan[i];
                float sustain = sustainSpan[i];
                float release = releaseSpan[i];
                double attackAndDecay = attack + decay;

                float level;
                if (time < releaseAt)
                {
                    if (attack > 0 && time < attack)
                        level = (float)(time / attack);
                    else if (decay > 0 && time < attackAndDecay)
                        level = 1.0f - (1.0f - sustain) * (float)((time - attack) / decay);
                    else
                        level = sustain;
                }
                else
                {
                    if (_releaseStartLevel < 0)
                        _releaseStartLevel = Envelope(releaseAt, attack, decay, sustain);

                    double intoRelease = time - releaseAt;
                    level = intoRelease >= release || release <= 0
                        ? 0f
                        : _releaseStartLevel * (float)(1.0 - intoRelease / release);
                }

                var frame = block[i];
                block[i] = new StereoFrame(frame.Left * level, frame.Right * level);
                time += period;
            }
        }

        _time = time;
    }

    // The level before any release: rise, fall to the sustain, hold it.
    private static float Envelope(double time, float attack, float decay, float sustain)
    {
        if (attack > 0 && time < attack)
            return (float)(time / attack);
        if (decay > 0 && time < attack + decay)
            return 1.0f - (1.0f - sustain) * (float)((time - attack) / decay);
        return sustain;
    }
}
