namespace Sigil.Sound.Operators;

/// <summary>
/// Plays a decoded <see cref="AudioBuffer"/> at a variable speed, blending between neighbouring frames so that
/// fractional positions sound smooth. Speed 1 plays at the file's own pitch and speed 2 an octave up. The playhead is
/// kept unwrapped, and the loop or ping-pong mapping is applied on read, so a loop never drifts and reversing is exact.
/// </summary>
internal sealed class SampleOperator : IOperator, IResettable
{
    private readonly StereoFrame[] _frames;
    private readonly ParamSource _speed;
    private readonly PlaybackMode _mode;

    // The region in frames that loops and ping-pong play. A one-shot always uses the whole sample.
    private readonly int _regionStart;
    private readonly int _regionEnd;

    // Converts a file frame to a render frame: a 24 kHz file played in a 48 kHz render runs at half the speed in frames.
    private readonly double _rateScale;

    private double _position;
    private bool _finished;
    private bool _started;

    public SampleOperator(StereoFrame[] frames, int fileSampleRate, ParamSource speed, PlaybackMode mode,
        double loopStartSeconds, double loopEndSeconds, RenderContext context)
    {
        _frames = frames;
        _speed = speed;
        _mode = mode;
        _rateScale = (double)fileSampleRate / context.SampleRate;

        int length = frames.Length;
        if (mode == PlaybackMode.OneShot)
        {
            _regionStart = 0;
            _regionEnd = length;
        }
        else
        {
            // Round the markers to whole frames and keep at least one frame inside the region.
            _regionEnd = (int)Math.Clamp(Math.Round(loopEndSeconds * fileSampleRate), 1, length);
            _regionStart = (int)Math.Clamp(Math.Round(loopStartSeconds * fileSampleRate), 0, _regionEnd - 1);
        }
    }

    public void Reset()
    {
        _position = 0;
        _finished = false;
        _started = false;
        _speed.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        var speed = _speed.Next(block.Length);

        // A one-shot played backward starts at its last frame and runs toward its first. A loop or ping-pong needs no
        // such start: its position wraps into the region from wherever it begins.
        if (!_started && block.Length > 0)
        {
            _started = true;
            if (_mode == PlaybackMode.OneShot && Dsp.Finite(speed[0], 0f) < 0)
                _position = _frames.Length - 1;
        }

        double position = _position;
        bool finished = _finished;
        double rateScale = _rateScale;
        int regionStart = _regionStart;
        int regionEnd = _regionEnd;
        int regionLength = regionEnd - regionStart;
        var frames = _frames;
        var mode = _mode;

        for (int i = 0; i < block.Length; i++)
        {
            if (finished)
            {
                block[i] = default;
                continue;
            }

            double mapped;
            if (mode == PlaybackMode.OneShot)
            {
                // The sample ends at its last frame; a playhead that moves backwards out of it ends the sample too.
                if (position < 0 || position >= frames.Length)
                {
                    finished = true;
                    block[i] = default;
                    continue;
                }
                mapped = position;
            }
            else if (mode == PlaybackMode.Loop)
            {
                mapped = regionStart + Modulo(position - regionStart, regionLength);
            }
            else
            {
                // Ping-pong folds the playhead back and forth across the region: forward, then backward, and so on.
                double folded = Modulo(position - regionStart, 2.0 * regionLength);
                mapped = folded < regionLength
                    ? regionStart + folded
                    : regionStart + (2.0 * regionLength - folded);
            }

            // Blend the frame under the playhead with the next one. At the region's edge a loop wraps to its start,
            // a ping-pong or a one-shot holds the last frame.
            // Rounding can put the mapped position exactly on the region end, so the index is kept inside the region.
            int index = Math.Min((int)mapped, regionEnd - 1);
            double fraction = Math.Clamp(mapped - index, 0.0, 1.0);
            int next = index + 1;
            if (next >= regionEnd)
                next = mode == PlaybackMode.Loop ? regionStart : index;
            var a = frames[index];
            var b = frames[next];
            float t = (float)fraction;
            block[i] = new StereoFrame(a.Left + (b.Left - a.Left) * t, a.Right + (b.Right - a.Right) * t);

            // Speed is read per frame, so a modulated speed bends the pitch smoothly.
            position += Dsp.Finite(speed[i], 0f) * rateScale;
        }

        _position = position;
        _finished = finished;
    }

    // A remainder that is always in [0, divisor), also for negative values, so the playhead wraps in both directions.
    private static double Modulo(double value, double divisor)
    {
        double remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}
