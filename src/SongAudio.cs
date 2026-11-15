using NLayer;

namespace Riffstorm;

/// <summary>Loads a library song's audio as 44.1 kHz channels: a multitrack VGS or a set of stereo MP3 stems.</summary>
public static class SongAudio
{
    public static short[][] Load(Song s)
    {
        if (s.Stems == null || s.Stems.Length == 0) return Vgs.LoadAll(s.AudioPath, out _);
        var parts = new short[s.Stems.Length][][];
        Parallel.For(0, s.Stems.Length, i => parts[i] = Mp3(Path.Combine(s.Dir, s.Stems[i])));
        return parts.SelectMany(p => p).ToArray();
    }

    /// <summary>Audio for the menu preview, plus the mix settings to play it with.</summary>
    public static (short[][] pcm, float[] pans, float[] vols, bool[] guitar) LoadPreview(Song s, double seconds)
    {
        if (!string.IsNullOrEmpty(s.Preview) && File.Exists(Path.Combine(s.Dir, s.Preview)))
        {
            var p = Mp3(Path.Combine(s.Dir, s.Preview), seconds);
            return (p, new[] { -1f, 1f }, new[] { 0.9f, 0.9f }, new bool[2]);
        }
        short[][] pcm;
        if (s.Stems == null || s.Stems.Length == 0) pcm = Vgs.LoadRange(s.AudioPath, s.PreviewStart, seconds, out _);
        else
        {
            var all = Load(s);
            int a = (int)(s.PreviewStart * Audio.Rate), n = (int)(seconds * Audio.Rate);
            pcm = all.Select(c => c.AsSpan(Math.Min(a, c.Length), Math.Max(0, Math.Min(n, c.Length - a))).ToArray()).ToArray();
        }
        return (pcm, s.Pans, s.Vols, new bool[pcm.Length]);
    }

    /// <summary>Decodes an MP3 file into left/right 16-bit channels (resampled to 44.1 kHz if needed).</summary>
    public static short[][] Mp3(string path, double maxSeconds = 0)
    {
        using var mp = new MpegFile(path);
        int ch = mp.Channels, rate = mp.SampleRate;
        var l = new List<short>(1 << 20); var r = new List<short>(1 << 20);
        var buf = new float[4096 * ch];
        long limit = maxSeconds > 0 ? (long)(maxSeconds * rate) : long.MaxValue;
        int n;
        while ((n = mp.ReadSamples(buf, 0, buf.Length)) > 0 && l.Count < limit)
        {
            for (int i = 0; i + ch - 1 < n; i += ch)
            {
                l.Add(S(buf[i]));
                r.Add(S(ch > 1 ? buf[i + 1] : buf[i]));
            }
        }
        var res = new[] { l.ToArray(), r.ToArray() };
        if (rate != Audio.Rate) for (int c = 0; c < 2; c++) res[c] = Vgs.Resample(res[c], rate, Audio.Rate);
        return res;
    }

    static short S(float f) => (short)Math.Clamp((int)(f * 32767f), -32768, 32767);
}
