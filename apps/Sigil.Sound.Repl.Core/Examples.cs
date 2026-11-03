namespace Sigil.Sound.Repl;

/// <summary>Starter snippets. Each one only uses what the library has today.</summary>
public static class Examples
{
    public sealed record Example(string Name, string Code);

    public static IReadOnlyList<Example> All { get; } =
    [
        new("Filter sweep",
            """
            // Press Ctrl+Enter to play. Edit anything and press it again:
            // the new sound crossfades in while the old one plays.
            var sweep = Sine(0.25).Range(200, 2000);   // a sound used as a parameter

            Saw(110)
                .LowPass(cutoff: sweep, q: 4)
                * 0.4
            """),
        new("Arpeggio",
            """
            // No note timing in the library yet, so time drives the pitch directly.
            var pitches = Notes.Parse("C3 E3 G3 B3 C4 B3 G3 E3")
                .Select(n => n.Frequency)
                .ToArray();
            var step = 0.125;   // seconds per note

            var pitch = Fn(t => pitches[(int)(t / step) % pitches.Length]);
            var pluck = Fn(t => Pow(1 - t % step / step, 3));

            Saw(pitch).LowPass(Sine(0.1).Range(500, 4000), q: 3) * pluck * 0.35
            """),
        new("Pulse width",
            """
            var width = Sine(0.3).Range(0.1, 0.9);

            (Square(55, width) + Square(110.4, width) * 0.5)
                .LowPass(1400, q: 1.5)
                * 0.3
            """),
        new("Ring modulation",
            """
            // Multiplying two sounds is ring modulation.
            var wobble = Sine(Fn(t => 2 + Sin(t * 0.7)));

            Sine(330) * Sine(Sine(0.2).Range(80, 120)) * wobble.Range(0.1, 0.5)
            """),
        new("Wind",
            """
            var gusts = Sine(0.07).Range(250, 1400);

            Noise().LowPass(gusts, q: 6) * Sine(0.13).Range(0.15, 0.5)
            """),
        new("Custom operator",
            """
            // Through hands you the running input and expects an operator back.
            // Operator.From writes one as a lambda; state lives in captured variables,
            // declared inside Through so every compile starts fresh.
            Saw(110).Through(input =>
            {
                var held = StereoFrame.Silence;
                var step = 0;

                return Operator.From(block =>
                {
                    input.RenderBlock(block);          // pull the input, then rework it in place
                    for (int i = 0; i != block.Length; i++)
                    {
                        if (step == 0) held = block[i];   // sample and hold: a crude downsampler
                        step = (step + 1) % 8;
                        block[i] = held;
                    }
                });
            })
            .LowPass(3000)
            * 0.3
            """),
        new("Drone",
            """
            Sound Voice(double hz, double detune) =>
                Saw(hz * (1 + detune)) + Saw(hz * (1 - detune));

            (Voice(55, 0.003) + Voice(82.5, 0.004) * 0.6 + Voice(110, 0.002) * 0.4)
                .LowPass(Sine(0.05).Range(300, 1800), q: 1.2)
                .Adsr(4, 0, 1, 0, hold: 9999)
                * 0.15
            """),
    ];
}
