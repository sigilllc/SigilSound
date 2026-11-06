using Sigil.Sound;

namespace SigilgraphHost;

/// <summary>How a faceplate control's travel maps to its value. The REPL sets it on the control as its <see cref="ControlTaper"/>.</summary>
public enum Curve
{
    /// <summary>Equal steps in value for equal travel.</summary>
    Linear,

    /// <summary>Equal ratios for equal travel, the natural scale for frequencies. The range must be above zero.</summary>
    Log,

    /// <summary>Slow at the bottom and fast at the top, for amounts that need fine control near zero.</summary>
    Exp,
}

/// <summary>
/// What a Sigilgraph Sound Script snippet sees besides Sigil.Sound, simulated for the browser: the same names and signatures as
/// the Workbench's device API (<c>Knob</c>, <c>Slider</c>, <c>Asset</c>, <c>In</c>, <c>Out</c>, <c>Poly</c>), so a snippet written
/// for the device runs here unchanged and the other way round.
/// </summary>
/// <remarks>
/// This is the REPL's copy of a contract owned by the Workbench (<c>AudioWorkbench.Scripting.Api.ScriptApi</c>), kept in step by
/// a test that compares the two public surfaces. It lives in the REPL, never in Sigil.Sound: the library knows
/// <see cref="Control"/>, and a Knob here is just a named control the page shows. Keep it small, since every public member
/// is something a pasted snippet can call. All of it records into the build running on the current thread
/// (<see cref="SigilgraphBuild"/>); nothing here renders audio.
/// </remarks>
public static class SigilgraphApi
{
    /// <summary>The device's inputs: <c>In.Audio</c> and <c>In.Control(0)</c>.</summary>
    public static InputApi In => SigilgraphBuild.Require().Input;

    /// <summary>The device's outputs: <c>Out.Control(0, sound)</c> and <c>Out.Notes(pattern)</c>.</summary>
    public static OutputApi Out => SigilgraphBuild.Require().Output;

    /// <summary>
    /// A knob on the faceplate, bound by <paramref name="name"/>. The sound is the knob's value, so it can be used anywhere a number
    /// goes: <c>LowPass(Knob("Cutoff", 1200, 40, 18000, Curve.Log))</c>.
    /// </summary>
    /// <param name="name">The label on the faceplate; also how the knob is found again after the code changes.</param>
    /// <param name="default">The value the knob starts at the first time this name appears. After that the knob keeps whatever you set.</param>
    /// <param name="min">The lowest value.</param>
    /// <param name="max">The highest value.</param>
    /// <param name="curve">How travel maps to value. Defaults to <see cref="Curve.Linear"/>.</param>
    public static Sound Knob(string name, double @default, double min, double max, Curve curve = Curve.Linear) =>
        SigilgraphBuild.Require().DeclareControl(name, @default, min, max, curve, isSlider: false);

    /// <summary>A slider on the faceplate; the same as <see cref="Knob"/> in every other way. The device has fewer sliders than knobs.</summary>
    /// <param name="name">The label on the faceplate.</param>
    /// <param name="default">The value it starts at the first time this name appears.</param>
    /// <param name="min">The lowest value.</param>
    /// <param name="max">The highest value.</param>
    /// <param name="curve">How travel maps to value. Defaults to <see cref="Curve.Linear"/>.</param>
    public static Sound Slider(string name, double @default, double min, double max, Curve curve = Curve.Linear) =>
        SigilgraphBuild.Require().DeclareControl(name, @default, min, max, curve, isSlider: true);

    /// <summary>
    /// A sample slot on the faceplate: a selector where you pick a sample. The result is the picked sample, ready for <c>Sample(...)</c>.
    /// </summary>
    /// <param name="name">The label on the faceplate; also how the pick is found again after the code changes.</param>
    /// <returns>The picked sample's audio at the file's own sample rate.</returns>
    public static AudioBuffer Asset(string name) => SigilgraphBuild.Require().DeclareAsset(name);

    /// <summary>
    /// Notes arriving at the device's note input, played as voices: each key starts a voice built from
    /// <paramref name="voice"/> that sustains while the key is down. In the REPL the notes come from the keyboard under the editor.
    /// </summary>
    /// <param name="polyphony">The most notes sounding at once, 1 to 32.</param>
    /// <param name="voice">Describes the sound of one note from its pitch, velocity and gate.</param>
    public static Sound Poly(int polyphony, Func<Voice, Sound> voice) =>
        SigilgraphBuild.Require().DeclarePoly(polyphony, voice);
}

/// <summary>The device's inputs, as sounds a snippet can use.</summary>
public sealed class InputApi
{
    private readonly SigilgraphBuild _build;

    internal InputApi(SigilgraphBuild build) => _build = build;

    /// <summary>The audio arriving at the device. In the REPL it is the test source chosen on the page.</summary>
    public Sound Audio => _build.AudioInput;

    /// <summary>A control signal arriving at the device. Zero until you move its slider on the page.</summary>
    /// <param name="index">Which control input, 0 to 3.</param>
    public Sound Control(int index) => _build.ControlInput(index);
}

/// <summary>The device's outputs, assigned from a snippet.</summary>
public sealed class OutputApi
{
    private readonly SigilgraphBuild _build;

    internal OutputApi(SigilgraphBuild build) => _build = build;

    /// <summary>Sends a sound out of one of the device's control outputs, for example an LFO to modulate another device's knob.</summary>
    /// <param name="index">Which control output, 0 or 1.</param>
    /// <param name="sound">The signal, nominally between -1 and 1.</param>
    public void Control(int index, Sound sound) => _build.SetControlOutput(index, sound);

    /// <summary>Sends notes out of the device's note output. In the REPL a plain voice plays them, since there is no device to send them to.</summary>
    /// <param name="pattern">The notes to play; a looping pattern stays on the beat grid.</param>
    public void Notes(Pattern pattern) => _build.SetNotesOutput(pattern);

    /// <summary>
    /// Sends a pattern of notes out of the device's note output. The pattern is asked for each block as it plays, so it can run
    /// forever and change from cycle to cycle. In the REPL a plain voice plays them.
    /// </summary>
    /// <param name="pattern">The notes to play.</param>
    public void Notes(Pattern<Note> pattern) => _build.SetLiveNotesOutput(pattern);
}
