namespace FlipClock;

/// <summary>
/// The alarm: the beep-beep-beep-beep of an old digital alarm clock.
/// The beep is calculated, not recorded: a 2 kHz tone with the slightly harsh edge of a small piezo buzzer.
/// It plays through the same sound stream as the flaps, also when the flap sound is switched off.
/// </summary>
internal static class AlarmSound
{
    private const int SampleRate = 44100;
    private static short[]? _beep;

    private static short[] Beep()
    {
        if (_beep != null) return _beep;
        const double hz = 2000, length = 0.085, attack = 0.003, release = 0.008;
        int n = (int)(SampleRate * length);
        var s = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate, w = 2 * Math.PI * hz * t;
            // a sine with some odd overtones: between a pure tone and a square wave, like a buzzer
            double v = Math.Sin(w) + 0.33 * Math.Sin(3 * w) + 0.16 * Math.Sin(5 * w);
            double env = Math.Min(1, t / attack) * Math.Min(1, (length - t) / release);
            s[i] = (short)(v / 1.3 * env * 0.5 * 32767);
        }
        return _beep = s;
    }

    /// <summary>Get the sound stream running before the alarm is due.</summary>
    public static void Warm() => FlapSound.WarmAlways();

    /// <summary>One second of alarm: four quick beeps, then a short rest. Call once a second while ringing.</summary>
    public static void RingOneSecond()
    {
        var beep = Beep();
        for (int i = 0; i < 4; i++) FlapSound.Play(beep, 20 + i * 125, 1f);
    }
}
