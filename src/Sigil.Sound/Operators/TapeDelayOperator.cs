namespace Sigil.Sound.Operators;

/// <summary>
/// An analog-style echo. The read head is slewed toward its target delay time rather than jumping to it, so a changing
/// delay produces the pitch glide of a tape machine instead of clicks. Each repeat passes through a one-pole low-pass,
/// which darkens it, and an asymmetric soft clipper, which compresses it and adds even harmonics, before it feeds back.
/// </summary>
internal sealed class TapeDelayOperator : IOperator, IResettable
{
    // How long the read head takes to cover most of a change in delay time. Slow enough to glide like tape, but a sweep
    // of a few hertz still tracks its target.
    private const double SlewSeconds = 0.08;

    // Shifts the clipper's curve, so its positive and negative halves differ: that asymmetry is what gives tape its warmth.
    private const float Asymmetry = 0.2f;

    // The blocker removes the DC offset the asymmetric clipper adds, with a corner near 5 Hz.
    private const double DcCornerHz = 5.0;

    private const float MaxFeedback = 0.95f;
    private const float MinDamping = 20f;

    private readonly IOperator _input;
    private readonly ParamSource _time;
    private readonly ParamSource _feedback;
    private readonly ParamSource _damping;
    private readonly ParamSource _drive;
    private readonly ParamSource _mix;
    private readonly DelayLine _line;
    private readonly double _sampleRate;
    private readonly float _slewCoefficient;
    private readonly float _dcPole;
    private readonly double _maxSeconds;
    private readonly bool _pingPong;
    private readonly CutoffCoefficient _damp;

    private double _readDelay;
    private bool _started;

    // One-pole low-pass state of the feedback path, one per channel.
    private float _lowLeft, _lowRight;

    // DC blocker state: the previous input and output, one pair per channel.
    private float _dcInLeft, _dcInRight, _dcOutLeft, _dcOutRight;

    public TapeDelayOperator(IOperator input, ParamSource time, ParamSource feedback, ParamSource damping,
        ParamSource drive, ParamSource mix, double maxSeconds, bool pingPong, RenderContext context)
    {
        _input = input;
        _time = time;
        _feedback = feedback;
        _damping = damping;
        _drive = drive;
        _mix = mix;
        _maxSeconds = maxSeconds;
        _pingPong = pingPong;
        _sampleRate = context.SampleRate;
        _damp = new CutoffCoefficient(context.SampleRate);
        _line = new DelayLine((int)Math.Ceiling(maxSeconds * context.SampleRate) + 2);
        _slewCoefficient = (float)(1.0 - Math.Exp(-1.0 / (SlewSeconds * context.SampleRate)));
        _dcPole = (float)Math.Exp(-2.0 * Math.PI * DcCornerHz / context.SampleRate);
    }

    public void Reset()
    {
        _line.Clear();
        _started = false;
        _lowLeft = _lowRight = 0;
        _dcInLeft = _dcInRight = _dcOutLeft = _dcOutRight = 0;
        _input.ResetState();
        _time.Reset();
        _feedback.Reset();
        _damping.Reset();
        _drive.Reset();
        _mix.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        if (block.Length == 0)
            return;

        var time = _time.Next(block.Length);
        var feedback = _feedback.Next(block.Length);
        var damping = _damping.Next(block.Length);
        var drive = _drive.Next(block.Length);
        var mix = _mix.Next(block.Length);

        double maxDelay = _line.MaxDelay;
        for (int i = 0; i < block.Length; i++)
        {
            var dry = block[i];

            // Damping is followed frame by frame, like the rest of the feedback settings; the cache makes a fixed cutoff free.
            double cutoff = Math.Clamp(Dsp.Finite(damping[i], 3000f), MinDamping, _sampleRate * 0.45);
            float dampCoefficient = _damp.For(cutoff);

            // The read head moves toward its target a little each frame, which is what makes the glide.
            double seconds = Math.Clamp(Dsp.Finite(time[i], 0.3f), 0.0, _maxSeconds);
            double target = Math.Clamp(seconds * _sampleRate, 1.0, maxDelay);
            if (!_started)
            {
                _readDelay = target;
                _started = true;
            }
            _readDelay += (target - _readDelay) * _slewCoefficient;
            var wet = _line.Read((float)_readDelay);

            // Feedback path: darken, then saturate, then remove the DC the saturator adds. Drive never goes below 1: a
            // smaller drive would stretch the curve's output past its ceiling, which is what makes a loop run hot.
            float driveAmount = Math.Max(1f, Dsp.Finite(drive[i], 1f));
            _lowLeft += (wet.Left - _lowLeft) * dampCoefficient;
            _lowRight += (wet.Right - _lowRight) * dampCoefficient;
            float satLeft = Saturate(_lowLeft, driveAmount);
            float satRight = Saturate(_lowRight, driveAmount);

            float blockedLeft = satLeft - _dcInLeft + _dcPole * _dcOutLeft;
            float blockedRight = satRight - _dcInRight + _dcPole * _dcOutRight;
            _dcInLeft = satLeft;
            _dcInRight = satRight;
            _dcOutLeft = blockedLeft;
            _dcOutRight = blockedRight;

            // Ping-pong crosses the feedback: each channel's repeats return to the other channel, so the echoes bounce.
            float fb = Math.Clamp(Dsp.Finite(feedback[i], 0.5f), 0f, MaxFeedback);
            var returned = _pingPong
                ? new StereoFrame(blockedRight * fb, blockedLeft * fb)
                : new StereoFrame(blockedLeft * fb, blockedRight * fb);
            _line.Write(dry + returned);
            block[i] = Dsp.Blend(dry, wet, mix[i]);
        }
    }

    // The gain of the curve at zero, tanh'(Asymmetry): dividing by it makes a small signal pass at exactly its own level,
    // so the loop loses nothing to the clipper when it is not overdriven.
    private static readonly float SmallSignalGain = 1f - MathF.Tanh(Asymmetry) * MathF.Tanh(Asymmetry);

    // Soft clipping with a shifted curve: tanh of the driven signal, minus its value at zero, scaled so that a small
    // signal comes out at exactly its own level. The blocker downstream removes the offset that subtraction leaves behind.
    private static float Saturate(float x, float drive) =>
        (MathF.Tanh(drive * x + Asymmetry) - MathF.Tanh(Asymmetry)) / (drive * SmallSignalGain);
}
