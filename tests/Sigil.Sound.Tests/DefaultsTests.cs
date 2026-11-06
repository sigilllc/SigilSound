using System.Reflection;
using System.Xml.Linq;
using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// The library is opinionated: the base case of every method works with no arguments, and what it does is a documented default.
/// These tests keep it that way. A new method that forces parameters on the user, or a default nobody wrote down, fails here.
/// </summary>
public class DefaultsTests
{
    private static readonly Sound Source = Saw(220) * 0.3;

    // --- the base case works -----------------------------------------------------------------------------------------------

    public static TheoryData<string> BaseCases() => new(BaseCaseSounds.Keys);

    private static readonly Dictionary<string, Sound> BaseCaseSounds = new()
    {
        ["Sine()"] = Sine(),
        ["Saw()"] = Saw(),
        ["Square()"] = Square(),
        ["Triangle()"] = Triangle(),
        ["Noise()"] = Noise(),
        ["Range()"] = Sine(2).Range(),
        ["Adsr()"] = Source.Adsr(),
        ["LowPass()"] = Source.LowPass(),
        ["HighPass()"] = Source.HighPass(),
        ["BandPass()"] = Source.BandPass(),
        ["Notch()"] = Source.Notch(),
        ["Peak()"] = Source.Peak(),
        ["LowShelf()"] = Source.LowShelf(),
        ["HighShelf()"] = Source.HighShelf(),
        ["Equalize()"] = Source.Equalize(),
        ["Delay()"] = Source.Delay(),
        ["Comb()"] = (Noise() * 0.3).Comb(),
        ["TapeDelay()"] = Source.TapeDelay(),
        ["Reverb()"] = Source.Reverb(),
        ["FdnReverb()"] = Source.FdnReverb(),
        ["Chorus()"] = Source.Chorus(),
        ["Phaser()"] = Source.Phaser(),
        ["Distort()"] = Source.Distort(),
        ["BitCrush()"] = Source.BitCrush(),
        ["Fold()"] = Source.Fold(),
        ["Compress()"] = Source.Compress(),
        ["Limit()"] = Source.Limit(),
        ["PitchShift()"] = Source.PitchShift(),
        ["Spread()"] = Source.Chorus().Spread(),
        ["Vowel()"] = Source.Vowel(),
        ["FollowEnvelope()"] = Source.FollowEnvelope(),
    };

    [Theory]
    [MemberData(nameof(BaseCases))]
    public void The_base_case_with_no_arguments_makes_sound(string name)
    {
        var audio = BaseCaseSounds[name].Render(1.5, Measure.SampleRate);

        AudioAssert.IsFinite(audio);
        Assert.True(SignalAnalysis.Peak(audio.ToMono()) > 0.01f, $"{name} is silent");
    }

    // --- what the defaults are ---------------------------------------------------------------------------------------------

    public static TheoryData<string> Spellings() => new(ExplicitSpellings.Keys);

    // Each default beside the explicit call it stands for. If a default changes, this and its XML comment change with it.
    private static readonly Dictionary<string, (Sound Short, Sound Long)> ExplicitSpellings = new()
    {
        ["Sine = 440 Hz"] = (Sine(), Sine(440)),
        ["Saw = 440 Hz"] = (Saw(), Saw(440)),
        ["Square = 440 Hz"] = (Square(), Square(440)),
        ["Triangle = 440 Hz"] = (Triangle(), Triangle(440)),
        ["Range = 0 to 1"] = (Sine(3).Range(), Sine(3).Range(0, 1)),
        ["Adsr = 0.01, 0.1, 0.7, 0.3"] = (Source.Adsr(), Source.Adsr(0.01, 0.1, 0.7, 0.3)),
        ["Adsr, attack only"] = (Source.Adsr(0.5), Source.Adsr(0.5, 0.1, 0.7, 0.3)),
        ["Adsr, modulated attack"] = (Source.Adsr(Sine(1).Range(0.01, 0.2)), Source.Adsr(Sine(1).Range(0.01, 0.2), 0.1, 0.7, 0.3)),
        ["LowPass = 1000 Hz"] = (Source.LowPass(), Source.LowPass(1000)),
        ["HighPass = 100 Hz"] = (Source.HighPass(), Source.HighPass(100)),
        ["BandPass = 1000 Hz"] = (Source.BandPass(), Source.BandPass(1000)),
        ["Notch = 1000 Hz"] = (Source.Notch(), Source.Notch(1000)),
        ["Peak = 1000 Hz, +4 dB"] = (Source.Peak(), Source.Peak(1000, gainDb: 4)),
        ["LowShelf = 200 Hz, +4 dB"] = (Source.LowShelf(), Source.LowShelf(200, gainDb: 4)),
        ["HighShelf = 5000 Hz, +4 dB"] = (Source.HighShelf(), Source.HighShelf(5000, gainDb: 4)),
        ["Delay = 0.375 s"] = (Source.Delay(), Source.Delay(0.375)),
        ["TapeDelay = 0.375 s"] = (Source.TapeDelay(), Source.TapeDelay(0.375)),
        ["Comb = 440 Hz"] = (Noise().Comb(), Noise().Comb(440)),
        ["PitchShift = 12"] = (Source.PitchShift(), Source.PitchShift(12)),
        ["Spread = 1.5"] = (Source.Chorus().Spread(), Source.Chorus().Spread(1.5)),
        ["Vowel = A"] = (Source.Vowel(), Source.Vowel(0)),
    };

    [Theory]
    [MemberData(nameof(Spellings))]
    public void A_default_is_what_the_explicit_call_would_say(string name)
    {
        var (shortCall, longCall) = ExplicitSpellings[name];

        Assert.Equal(longCall.Render(0.5, Measure.SampleRate).Left(), shortCall.Render(0.5, Measure.SampleRate).Left());
    }

    [Fact]
    public void Peak_and_the_shelves_with_no_arguments_audibly_change_the_sound()
    {
        // A default of 0 dB would make the base case a no-op, which is the thing this policy exists to avoid.
        var plain = Source.Render(0.5, Measure.SampleRate).Left();

        foreach (var shaped in new[] { Source.Peak(), Source.LowShelf(), Source.HighShelf() })
            Assert.NotEqual(plain, shaped.Render(0.5, Measure.SampleRate).Left());
    }

    // --- the policy --------------------------------------------------------------------------------------------------------

    // Parameters a user must give, with the reason there is nothing sensible to default to.
    private static readonly HashSet<string> RequiredOnPurpose =
    [
        "Sounds.Fn(function)",                           // the function is the sound
        "Sounds.Constant(value)",                        // a constant without a value is nothing
        "Sounds.Sample(buffer)",                         // there is no default recording
        "Sounds.FromParam(parameter)",                   // converts what it is given
        "Sound.FromOperator(factory)",                   // the operator is the sound
        "Sound.Through(wrap)",                           // the operator is the effect
        "Sound.Gain(gain)",                              // 1 does nothing and any other level is a guess
        "Sound.Pan(position)",                           // centre does nothing and any side is a guess
        "Sound.Balance(position)",                       // the same
        "Sound.Feedback(loop)",                          // the loop is the effect
        "Sound.Convolve(impulse)",                       // there is no default impulse response
        "Sound.Adsr(attack)",                            // the overload that takes sounds; Adsr() with no arguments picks the all-default one that takes numbers
        "Sounds.Sample(frequency)",                      // the pitch-mapped overload: where the recording was pitched is a fact about the file
        "Sounds.Sample(rootFrequency)",                  // the same
        "Sounds.Fn(rate)",                               // the overload with a rate; Fn(function) is the base case
    ];

    private static IEnumerable<MethodInfo> FluentMethods() =>
        new[] { typeof(Sound), typeof(Sounds) }
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName && m.ReturnType == typeof(Sound) && m.Name != "ToString");

    [Fact]
    public void A_method_only_requires_parameters_that_have_no_sensible_default()
    {
        var offenders = new List<string>();
        foreach (var method in FluentMethods())
        {
            // The overload that takes a number (or an AudioBuffer plus more) is covered by the one that takes the first parameter alone.
            var required = method.GetParameters().Where(p => !p.HasDefaultValue).Select(p => p.Name!).ToList();
            if (required.Count == 0)
                continue;

            string owner = method.DeclaringType!.Name;
            bool allowed = required.All(p => RequiredOnPurpose.Contains($"{owner}.{method.Name}({p})"));
            if (!allowed)
                offenders.Add($"{owner}.{method.Name}({string.Join(", ", required)})");
        }

        Assert.True(offenders.Count == 0,
            "These methods force parameters on the user. Give each a documented default, or add it to RequiredOnPurpose with the reason:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void Every_default_is_written_down_in_the_docs_of_its_parameter()
    {
        var xml = XDocument.Load(Path.ChangeExtension(typeof(Sound).Assembly.Location, ".xml"));
        var docs = xml.Descendants("member").ToDictionary(m => m.Attribute("name")!.Value);

        var undocumented = new List<string>();
        foreach (var method in FluentMethods())
        {
            if (!docs.TryGetValue(MemberId(method), out var member))
            {
                undocumented.Add($"{method.DeclaringType!.Name}.{method.Name}: no XML comment found ({MemberId(method)})");
                continue;
            }

            foreach (var parameter in method.GetParameters().Where(p => p.HasDefaultValue))
            {
                var text = member.Elements("param").FirstOrDefault(p => (string?)p.Attribute("name") == parameter.Name)?.Value ?? "";
                if (!text.Contains("Default", StringComparison.Ordinal) && !text.Contains("default", StringComparison.Ordinal))
                    undocumented.Add($"{method.DeclaringType!.Name}.{method.Name}({parameter.Name})");
            }
        }

        Assert.True(undocumented.Count == 0,
            "These optional parameters do not say what they default to; add \"Defaults to X.\" to their <param>:\n" + string.Join("\n", undocumented));
    }

    // The id the XML documentation file uses for a method: M:Namespace.Type.Name(ParameterTypes).
    private static string MemberId(MethodInfo method)
    {
        var parameters = method.GetParameters();
        string list = parameters.Length == 0 ? "" : "(" + string.Join(",", parameters.Select(p => TypeId(p.ParameterType))) + ")";
        return $"M:{method.DeclaringType!.FullName}.{method.Name}{list}";
    }

    private static string TypeId(Type type)
    {
        if (!type.IsGenericType)
            return type.FullName!;
        string name = type.GetGenericTypeDefinition().FullName!;
        name = name[..name.IndexOf('`')];
        return $"{name}{{{string.Join(",", type.GetGenericArguments().Select(TypeId))}}}";
    }
}
