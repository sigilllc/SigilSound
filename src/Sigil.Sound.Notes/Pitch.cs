namespace Sigil.Sound;

/// <summary>
/// MIDI pitches by name, for writing notes in code: <c>using static Sigil.Sound.Pitch;</c> then <c>Scale.Minor(C3)</c> or
/// <c>new Note(Eb4)</c>. A trailing <c>s</c> is a sharp and <c>b</c> a flat, so <c>Cs4</c> is C sharp and <c>Db4</c> is D flat.
/// Octave 4 holds middle C (<c>C4</c> is 60); the range is octave 0 to octave 8.
/// </summary>
public static class Pitch
{
    /// <summary>MIDI pitch 12.</summary>
    public const int C0 = 12;
    /// <summary>MIDI pitch 13.</summary>
    public const int Cs0 = 13;
    /// <summary>MIDI pitch 13.</summary>
    public const int Db0 = 13;
    /// <summary>MIDI pitch 14.</summary>
    public const int D0 = 14;
    /// <summary>MIDI pitch 15.</summary>
    public const int Ds0 = 15;
    /// <summary>MIDI pitch 15.</summary>
    public const int Eb0 = 15;
    /// <summary>MIDI pitch 16.</summary>
    public const int E0 = 16;
    /// <summary>MIDI pitch 17.</summary>
    public const int F0 = 17;
    /// <summary>MIDI pitch 18.</summary>
    public const int Fs0 = 18;
    /// <summary>MIDI pitch 18.</summary>
    public const int Gb0 = 18;
    /// <summary>MIDI pitch 19.</summary>
    public const int G0 = 19;
    /// <summary>MIDI pitch 20.</summary>
    public const int Gs0 = 20;
    /// <summary>MIDI pitch 20.</summary>
    public const int Ab0 = 20;
    /// <summary>MIDI pitch 21.</summary>
    public const int A0 = 21;
    /// <summary>MIDI pitch 22.</summary>
    public const int As0 = 22;
    /// <summary>MIDI pitch 22.</summary>
    public const int Bb0 = 22;
    /// <summary>MIDI pitch 23.</summary>
    public const int B0 = 23;

    /// <summary>MIDI pitch 24.</summary>
    public const int C1 = 24;
    /// <summary>MIDI pitch 25.</summary>
    public const int Cs1 = 25;
    /// <summary>MIDI pitch 25.</summary>
    public const int Db1 = 25;
    /// <summary>MIDI pitch 26.</summary>
    public const int D1 = 26;
    /// <summary>MIDI pitch 27.</summary>
    public const int Ds1 = 27;
    /// <summary>MIDI pitch 27.</summary>
    public const int Eb1 = 27;
    /// <summary>MIDI pitch 28.</summary>
    public const int E1 = 28;
    /// <summary>MIDI pitch 29.</summary>
    public const int F1 = 29;
    /// <summary>MIDI pitch 30.</summary>
    public const int Fs1 = 30;
    /// <summary>MIDI pitch 30.</summary>
    public const int Gb1 = 30;
    /// <summary>MIDI pitch 31.</summary>
    public const int G1 = 31;
    /// <summary>MIDI pitch 32.</summary>
    public const int Gs1 = 32;
    /// <summary>MIDI pitch 32.</summary>
    public const int Ab1 = 32;
    /// <summary>MIDI pitch 33.</summary>
    public const int A1 = 33;
    /// <summary>MIDI pitch 34.</summary>
    public const int As1 = 34;
    /// <summary>MIDI pitch 34.</summary>
    public const int Bb1 = 34;
    /// <summary>MIDI pitch 35.</summary>
    public const int B1 = 35;

    /// <summary>MIDI pitch 36.</summary>
    public const int C2 = 36;
    /// <summary>MIDI pitch 37.</summary>
    public const int Cs2 = 37;
    /// <summary>MIDI pitch 37.</summary>
    public const int Db2 = 37;
    /// <summary>MIDI pitch 38.</summary>
    public const int D2 = 38;
    /// <summary>MIDI pitch 39.</summary>
    public const int Ds2 = 39;
    /// <summary>MIDI pitch 39.</summary>
    public const int Eb2 = 39;
    /// <summary>MIDI pitch 40.</summary>
    public const int E2 = 40;
    /// <summary>MIDI pitch 41.</summary>
    public const int F2 = 41;
    /// <summary>MIDI pitch 42.</summary>
    public const int Fs2 = 42;
    /// <summary>MIDI pitch 42.</summary>
    public const int Gb2 = 42;
    /// <summary>MIDI pitch 43.</summary>
    public const int G2 = 43;
    /// <summary>MIDI pitch 44.</summary>
    public const int Gs2 = 44;
    /// <summary>MIDI pitch 44.</summary>
    public const int Ab2 = 44;
    /// <summary>MIDI pitch 45.</summary>
    public const int A2 = 45;
    /// <summary>MIDI pitch 46.</summary>
    public const int As2 = 46;
    /// <summary>MIDI pitch 46.</summary>
    public const int Bb2 = 46;
    /// <summary>MIDI pitch 47.</summary>
    public const int B2 = 47;

    /// <summary>MIDI pitch 48.</summary>
    public const int C3 = 48;
    /// <summary>MIDI pitch 49.</summary>
    public const int Cs3 = 49;
    /// <summary>MIDI pitch 49.</summary>
    public const int Db3 = 49;
    /// <summary>MIDI pitch 50.</summary>
    public const int D3 = 50;
    /// <summary>MIDI pitch 51.</summary>
    public const int Ds3 = 51;
    /// <summary>MIDI pitch 51.</summary>
    public const int Eb3 = 51;
    /// <summary>MIDI pitch 52.</summary>
    public const int E3 = 52;
    /// <summary>MIDI pitch 53.</summary>
    public const int F3 = 53;
    /// <summary>MIDI pitch 54.</summary>
    public const int Fs3 = 54;
    /// <summary>MIDI pitch 54.</summary>
    public const int Gb3 = 54;
    /// <summary>MIDI pitch 55.</summary>
    public const int G3 = 55;
    /// <summary>MIDI pitch 56.</summary>
    public const int Gs3 = 56;
    /// <summary>MIDI pitch 56.</summary>
    public const int Ab3 = 56;
    /// <summary>MIDI pitch 57.</summary>
    public const int A3 = 57;
    /// <summary>MIDI pitch 58.</summary>
    public const int As3 = 58;
    /// <summary>MIDI pitch 58.</summary>
    public const int Bb3 = 58;
    /// <summary>MIDI pitch 59.</summary>
    public const int B3 = 59;

    /// <summary>MIDI pitch 60.</summary>
    public const int C4 = 60;
    /// <summary>MIDI pitch 61.</summary>
    public const int Cs4 = 61;
    /// <summary>MIDI pitch 61.</summary>
    public const int Db4 = 61;
    /// <summary>MIDI pitch 62.</summary>
    public const int D4 = 62;
    /// <summary>MIDI pitch 63.</summary>
    public const int Ds4 = 63;
    /// <summary>MIDI pitch 63.</summary>
    public const int Eb4 = 63;
    /// <summary>MIDI pitch 64.</summary>
    public const int E4 = 64;
    /// <summary>MIDI pitch 65.</summary>
    public const int F4 = 65;
    /// <summary>MIDI pitch 66.</summary>
    public const int Fs4 = 66;
    /// <summary>MIDI pitch 66.</summary>
    public const int Gb4 = 66;
    /// <summary>MIDI pitch 67.</summary>
    public const int G4 = 67;
    /// <summary>MIDI pitch 68.</summary>
    public const int Gs4 = 68;
    /// <summary>MIDI pitch 68.</summary>
    public const int Ab4 = 68;
    /// <summary>MIDI pitch 69.</summary>
    public const int A4 = 69;
    /// <summary>MIDI pitch 70.</summary>
    public const int As4 = 70;
    /// <summary>MIDI pitch 70.</summary>
    public const int Bb4 = 70;
    /// <summary>MIDI pitch 71.</summary>
    public const int B4 = 71;

    /// <summary>MIDI pitch 72.</summary>
    public const int C5 = 72;
    /// <summary>MIDI pitch 73.</summary>
    public const int Cs5 = 73;
    /// <summary>MIDI pitch 73.</summary>
    public const int Db5 = 73;
    /// <summary>MIDI pitch 74.</summary>
    public const int D5 = 74;
    /// <summary>MIDI pitch 75.</summary>
    public const int Ds5 = 75;
    /// <summary>MIDI pitch 75.</summary>
    public const int Eb5 = 75;
    /// <summary>MIDI pitch 76.</summary>
    public const int E5 = 76;
    /// <summary>MIDI pitch 77.</summary>
    public const int F5 = 77;
    /// <summary>MIDI pitch 78.</summary>
    public const int Fs5 = 78;
    /// <summary>MIDI pitch 78.</summary>
    public const int Gb5 = 78;
    /// <summary>MIDI pitch 79.</summary>
    public const int G5 = 79;
    /// <summary>MIDI pitch 80.</summary>
    public const int Gs5 = 80;
    /// <summary>MIDI pitch 80.</summary>
    public const int Ab5 = 80;
    /// <summary>MIDI pitch 81.</summary>
    public const int A5 = 81;
    /// <summary>MIDI pitch 82.</summary>
    public const int As5 = 82;
    /// <summary>MIDI pitch 82.</summary>
    public const int Bb5 = 82;
    /// <summary>MIDI pitch 83.</summary>
    public const int B5 = 83;

    /// <summary>MIDI pitch 84.</summary>
    public const int C6 = 84;
    /// <summary>MIDI pitch 85.</summary>
    public const int Cs6 = 85;
    /// <summary>MIDI pitch 85.</summary>
    public const int Db6 = 85;
    /// <summary>MIDI pitch 86.</summary>
    public const int D6 = 86;
    /// <summary>MIDI pitch 87.</summary>
    public const int Ds6 = 87;
    /// <summary>MIDI pitch 87.</summary>
    public const int Eb6 = 87;
    /// <summary>MIDI pitch 88.</summary>
    public const int E6 = 88;
    /// <summary>MIDI pitch 89.</summary>
    public const int F6 = 89;
    /// <summary>MIDI pitch 90.</summary>
    public const int Fs6 = 90;
    /// <summary>MIDI pitch 90.</summary>
    public const int Gb6 = 90;
    /// <summary>MIDI pitch 91.</summary>
    public const int G6 = 91;
    /// <summary>MIDI pitch 92.</summary>
    public const int Gs6 = 92;
    /// <summary>MIDI pitch 92.</summary>
    public const int Ab6 = 92;
    /// <summary>MIDI pitch 93.</summary>
    public const int A6 = 93;
    /// <summary>MIDI pitch 94.</summary>
    public const int As6 = 94;
    /// <summary>MIDI pitch 94.</summary>
    public const int Bb6 = 94;
    /// <summary>MIDI pitch 95.</summary>
    public const int B6 = 95;

    /// <summary>MIDI pitch 96.</summary>
    public const int C7 = 96;
    /// <summary>MIDI pitch 97.</summary>
    public const int Cs7 = 97;
    /// <summary>MIDI pitch 97.</summary>
    public const int Db7 = 97;
    /// <summary>MIDI pitch 98.</summary>
    public const int D7 = 98;
    /// <summary>MIDI pitch 99.</summary>
    public const int Ds7 = 99;
    /// <summary>MIDI pitch 99.</summary>
    public const int Eb7 = 99;
    /// <summary>MIDI pitch 100.</summary>
    public const int E7 = 100;
    /// <summary>MIDI pitch 101.</summary>
    public const int F7 = 101;
    /// <summary>MIDI pitch 102.</summary>
    public const int Fs7 = 102;
    /// <summary>MIDI pitch 102.</summary>
    public const int Gb7 = 102;
    /// <summary>MIDI pitch 103.</summary>
    public const int G7 = 103;
    /// <summary>MIDI pitch 104.</summary>
    public const int Gs7 = 104;
    /// <summary>MIDI pitch 104.</summary>
    public const int Ab7 = 104;
    /// <summary>MIDI pitch 105.</summary>
    public const int A7 = 105;
    /// <summary>MIDI pitch 106.</summary>
    public const int As7 = 106;
    /// <summary>MIDI pitch 106.</summary>
    public const int Bb7 = 106;
    /// <summary>MIDI pitch 107.</summary>
    public const int B7 = 107;

    /// <summary>MIDI pitch 108.</summary>
    public const int C8 = 108;
    /// <summary>MIDI pitch 109.</summary>
    public const int Cs8 = 109;
    /// <summary>MIDI pitch 109.</summary>
    public const int Db8 = 109;
    /// <summary>MIDI pitch 110.</summary>
    public const int D8 = 110;
    /// <summary>MIDI pitch 111.</summary>
    public const int Ds8 = 111;
    /// <summary>MIDI pitch 111.</summary>
    public const int Eb8 = 111;
    /// <summary>MIDI pitch 112.</summary>
    public const int E8 = 112;
    /// <summary>MIDI pitch 113.</summary>
    public const int F8 = 113;
    /// <summary>MIDI pitch 114.</summary>
    public const int Fs8 = 114;
    /// <summary>MIDI pitch 114.</summary>
    public const int Gb8 = 114;
    /// <summary>MIDI pitch 115.</summary>
    public const int G8 = 115;
    /// <summary>MIDI pitch 116.</summary>
    public const int Gs8 = 116;
    /// <summary>MIDI pitch 116.</summary>
    public const int Ab8 = 116;
    /// <summary>MIDI pitch 117.</summary>
    public const int A8 = 117;
    /// <summary>MIDI pitch 118.</summary>
    public const int As8 = 118;
    /// <summary>MIDI pitch 118.</summary>
    public const int Bb8 = 118;
    /// <summary>MIDI pitch 119.</summary>
    public const int B8 = 119;
}
