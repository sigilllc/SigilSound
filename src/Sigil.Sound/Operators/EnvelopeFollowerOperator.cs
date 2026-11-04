namespace Sigil.Sound.Operators;

/// <summary>
/// Turns an audio signal into a slow control signal that follows its loudness, from 0 (silent) to 1 (full scale).
/// It is a peak follower: when the level rises it closes the gap with the attack time constant, and when it falls with the
/// release time constant. Both are exponential, so after one time constant the output has covered 63% of a step.
/// </summary>
internal sealed class EnvelopeFollowerOperator : IOperator, IResettable
{
    private const float DenormalFloor = 1e-15f;

    private readonly IOperator _input;
    private readonly ParamSource _attack;
    private readonly ParamSource _release;
    private readonly SmoothingCoefficient _attackCoefficient;
    private readonly SmoothingCoefficient _releaseCoefficient;
    private float _envelope;

    public EnvelopeFollowerOperator(IOperator input, ParamSource attack, ParamSource release, RenderContext context)
    {
        _input = input;
        _attack = attack;
        _release = release;
        _attackCoefficient = new SmoothingCoefficient(context.SampleRate);
        _releaseCoefficient = new SmoothingCoefficient(context.SampleRate);
    }

    public void Reset()
    {
        _envelope = 0f;
        _input.ResetState();
        _attack.Reset();
        _release.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var attack = _attack.Next(block.Length);
        var release = _release.Next(block.Length);

        float envelope = _envelope;
        for (int i = 0; i < block.Length; i++)
        {
            // The larger channel drives the follower, so a loud channel is never hidden by a quiet one.
            var frame = block[i];
            float level = MathF.Max(MathF.Abs(Dsp.Safe(frame.Left)), MathF.Abs(Dsp.Safe(frame.Right)));

            float coefficient = level > envelope
                ? _attackCoefficient.For(Dsp.Finite(attack[i], 0.01f))
                : _releaseCoefficient.For(Dsp.Finite(release[i], 0.1f));
            envelope = level + coefficient * (envelope - level);
            if (envelope < DenormalFloor)
                envelope = 0f;

            block[i] = StereoFrame.Mono(Math.Clamp(envelope, 0f, 1f));
        }

        _envelope = envelope;
    }
}
