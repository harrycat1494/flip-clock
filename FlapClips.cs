namespace FlipClock;

/// <summary>
/// Hands the recorded clacks to the sound player. They travel inside the program file
/// (Assets/flap/*.wav), so there is nothing extra to install.
/// </summary>
internal static class FlapClips
{
    /// <summary>Switch the flap sound on or off. The clacks are only loaded the first time it is switched on.</summary>
    public static void Enable(bool on)
    {
        FlapSound.Enabled = on;
        if (on && !FlapSound.HasClips) Task.Run(Load); // off the screen thread: no stutter
    }

    private static void Load()
    {
        var asm = typeof(FlapClips).Assembly;
        var clips = new List<short[]>();
        foreach (string name in asm.GetManifestResourceNames()
                     .Where(n => n.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n))
        {
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null || stream.Length <= 44) continue;
            byte[] wav = new byte[stream.Length];
            stream.ReadExactly(wav);
            // our own files: a plain 44-byte header, then mono 16-bit samples
            var samples = new short[(wav.Length - 44) / 2];
            Buffer.BlockCopy(wav, 44, samples, 0, samples.Length * 2);
            clips.Add(samples);
        }
        FlapSound.SetClips(clips.ToArray());
    }
}
