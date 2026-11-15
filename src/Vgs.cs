namespace Riffstorm;

/// <summary>
/// Harmonix VGS v2: 0x80 header (up to 15 channels: sample rate + block count each), then 16-byte PS-ADPCM frames.
/// The frame's flag byte holds the channel index, so channels with different sample rates (e.g. a half-rate bass
/// track) are simply routed by that byte. Everything is resampled to the mixer rate (44.1 kHz).
/// </summary>
public static class Vgs
{
    static readonly int[] C0 = { 0, 60, 115, 98, 122 };
    static readonly int[] C1 = { 0, 0, -52, -55, -60 };

    public sealed record Info(int[] Rates, int[] Blocks)
    {
        public int Channels => Rates.Length;
    }

    public static Info Header(byte[] h)
    {
        if (h[0] != 'V' || h[1] != 'g' || h[2] != 'S' || h[3] != '!') throw new InvalidDataException("Áudio VGS inválido.");
        var rates = new List<int>(); var blocks = new List<int>();
        for (int i = 0; i < 15; i++)
        {
            int r = BitConverter.ToInt32(h, 8 + i * 8), b = BitConverter.ToInt32(h, 12 + i * 8);
            if (r == 0) break;
            rates.Add(r); blocks.Add(b);
        }
        return new Info(rates.ToArray(), blocks.ToArray());
    }

    /// <summary>Decodes all frames, routing each one to the channel named in its flag byte (native rates).</summary>
    public static short[][] DecodeNative(byte[] data, int dataOffset, Info info)
    {
        int nch = info.Channels;
        var outp = new short[nch][];
        for (int c = 0; c < nch; c++) outp[c] = new short[(long)info.Blocks[c] * 28 > int.MaxValue ? 0 : info.Blocks[c] * 28];
        var w = new int[nch]; var h1 = new int[nch]; var h2 = new int[nch];
        for (int p = dataOffset; p + 16 <= data.Length; p += 16)
        {
            int c = data[p + 1];
            if (c >= nch) continue;
            var o = outp[c];
            if (w[c] + 28 > o.Length) continue;
            int shift = data[p] & 0xF, pred = data[p] >> 4;
            if (pred > 4) pred = 0;
            if (shift > 12) shift = 9;
            int c0 = C0[pred], c1 = C1[pred], a = h1[c], b = h2[c], wi = w[c];
            for (int i = 0; i < 28; i++)
            {
                int by = data[p + 2 + (i >> 1)];
                int nib = (i & 1) == 0 ? by & 0xF : by >> 4;
                int s = ((short)(nib << 12)) >> shift;
                s += (a * c0 + b * c1 + 32) >> 6;
                if (s > 32767) s = 32767; else if (s < -32768) s = -32768;
                b = a; a = s;
                o[wi++] = (short)s;
            }
            h1[c] = a; h2[c] = b; w[c] = wi;
        }
        for (int c = 0; c < nch; c++) if (w[c] < outp[c].Length) Array.Resize(ref outp[c], w[c]);
        return outp;
    }

    /// <summary>Catmull-Rom resampling of one channel to the target rate.</summary>
    public static short[] Resample(short[] src, int from, int to)
    {
        if (from == to || src.Length < 4) return src;
        long n = (long)src.Length * to / from;
        var dst = new short[n];
        double step = from / (double)to;
        int last = src.Length - 1;
        for (long i = 0; i < n; i++)
        {
            double x = i * step;
            int k = (int)x; float t = (float)(x - k);
            float p0 = src[Math.Max(0, k - 1)], p1 = src[Math.Min(last, k)], p2 = src[Math.Min(last, k + 1)], p3 = src[Math.Min(last, k + 2)];
            float v = p1 + 0.5f * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
            dst[i] = (short)Math.Clamp(v, -32768, 32767);
        }
        return dst;
    }

    /// <summary>Whole song, every channel at 44.1 kHz.</summary>
    public static short[][] LoadAll(string path, out int rate)
    {
        var data = File.ReadAllBytes(path);
        var info = Header(data);
        var ch = DecodeNative(data, 0x80, info);
        rate = Audio.Rate;
        for (int c = 0; c < ch.Length; c++) ch[c] = Resample(ch[c], info.Rates[c], Audio.Rate);
        return ch;
    }

    /// <summary>A window of the song (menu previews).</summary>
    public static short[][] LoadRange(string path, double startSec, double lenSec, out int rate)
    {
        var all = LoadAll(path, out rate);
        int s = (int)Math.Max(0, startSec * rate), n = (int)Math.Max(0, lenSec * rate);
        var res = new short[all.Length][];
        for (int c = 0; c < all.Length; c++)
        {
            int a = Math.Min(s, all[c].Length), len = Math.Min(n, all[c].Length - a);
            res[c] = new short[len];
            Array.Copy(all[c], a, res[c], 0, len);
        }
        return res;
    }
}
