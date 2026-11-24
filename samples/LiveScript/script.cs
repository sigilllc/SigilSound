// Edit this file and save it: the new version fades in over the old one while it plays.
// Play notes with A S D F G H J K (W E T Y U for the black keys, Z and X for the octave).
var drone = (Saw(55) + Saw(55.3)).LowPass(Sine(0.1).Range(200, 900), q: 3) * 0.15;

var keys = Keys.Play(v => Square(v.Frequency, width: 0.2).DcBlock()
    .LowPass(2400, q: 2)
    .Adsr(0.005, 0.2, 0.5, 0.4));

(drone + keys * 0.3).Reverb(mix: 0.25).Limit()
