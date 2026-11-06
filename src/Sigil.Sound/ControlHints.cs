namespace Sigil.Sound;

/// <summary>
/// How a UI's travel (a knob turn or a fader position, 0 to 1) maps onto a <see cref="Control"/>'s value. It describes the
/// hand, not the sound: it never changes what the control does once it has a value, so it is a hint a host UI reads and
/// <see cref="Control.Travel"/> applies. Compare <see cref="ControlCurve"/>, which maps a control's value onto another range.
/// </summary>
public enum ControlTaper
{
    /// <summary>Equal travel gives equal steps in value.</summary>
    Linear,

    /// <summary>Equal travel gives equal ratios, which is how pitch and cutoff are heard. The minimum must be above zero.</summary>
    Log,

    /// <summary>Slow at the bottom and fast at the top: the travel is squared, for amounts that need fine control near the minimum.</summary>
    Exp,
}

/// <summary>What a signal sent to <see cref="Control.SetFromSignal"/> spans.</summary>
public enum ControlPolarity
{
    /// <summary>The signal runs from 0 to 1 and 0 is the control's minimum, like an envelope or a unipolar LFO.</summary>
    Unipolar,

    /// <summary>The signal runs from -1 to 1 and 0 is the middle of the control's travel, like a bipolar LFO.</summary>
    Bipolar,
}

/// <summary>A hint to a host UI about which widget suits a <see cref="Control"/>. A host may ignore it.</summary>
public enum ControlStyle
{
    /// <summary>A rotary knob: compact, good for many controls.</summary>
    Knob,

    /// <summary>A slider or fader: a long travel, for a control that deserves more room.</summary>
    Slider,

    /// <summary>A pick from a few named options, such as a waveform. The value is the position in <see cref="Control.Options"/>.</summary>
    Selector,
}
