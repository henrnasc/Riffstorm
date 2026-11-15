using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Riffstorm;

/// <summary>
/// Single always-on float stereo stream. Mixes the song's channels (with the guitar
/// channels on their own gain so misses mute the guitar, like the original) plus one-shot SFX.
/// </summary>
public static unsafe class Audio
{
    public const int Rate = 44100;
    const int Buffer = 512;

    static AudioStream _stream;

    // song state (written by main thread, read by audio thread)
    static short[][] _ch;
    static float[] _gL, _gR;
    static bool[] _guitar;
    static long _len;
    static volatile bool _playing, _paused;
    static long _pos;                       // frame position, may be negative (lead-in)
    static float _gtrVol = 1, _gtrTarget = 1;
    public static float MusicVolume = 0.9f, GuitarBoost = 1f, SfxVolume = 0.8f;
    public static bool Loop;
    static float _fade = 1, _fadeTarget = 1;
    // whammy: guitar stems go through a modulated fractional delay -> pitch wobble, never drifts out of sync
    public static volatile float WhammyTarget;
    static float _wham;
    static readonly float[] _dl = new float[4096], _dr = new float[4096];
    static int _dIdx;
    static double _lfo;

    // timing
    static readonly Stopwatch Sw = Stopwatch.StartNew();
    static long _cbPos;
    static double _cbAt;

    // sfx
    struct Voice { public float[] Data; public int Pos; public float Vol; public float Pan; }
    static readonly Voice[] Voices = new Voice[16];
    static readonly object SfxLock = new();

    public static void Init()
    {
        Raylib.InitAudioDevice();
        Raylib.SetAudioStreamBufferSizeDefault(Buffer);
        _stream = Raylib.LoadAudioStream(Rate, 32, 2);
        Raylib.SetAudioStreamCallback(_stream, &Callback);
        Raylib.PlayAudioStream(_stream);
    }

    public static void Shutdown()
    {
        Raylib.StopAudioStream(_stream);
        Raylib.UnloadAudioStream(_stream);
        Raylib.CloseAudioDevice();
    }

    public static void SetSong(short[][] ch, float[] pans, float[] vols, bool[] guitar, double startSec)
    {
        _playing = false;
        int n = ch.Length;
        var gl = new float[n]; var gr = new float[n];
        for (int i = 0; i < n; i++)
        {
            float pan = i < pans.Length ? pans[i] : (i % 2 == 0 ? -1 : 1);
            float v = (i < vols.Length ? vols[i] : 1f) / 32768f;
            float a = (pan + 1) * 0.25f * MathF.PI;           // constant-power pan
            gl[i] = MathF.Cos(a) * v * 1.2f; gr[i] = MathF.Sin(a) * v * 1.2f;
        }
        long len = long.MaxValue;
        foreach (var c in ch) len = Math.Min(len, c.Length);
        _ch = ch; _gL = gl; _gR = gr; _guitar = guitar; _len = len;
        _pos = (long)(startSec * Rate);
        _cbPos = _pos; _cbAt = Sw.Elapsed.TotalSeconds;
        _gtrVol = _gtrTarget = 1; _paused = false;
        _fade = _fadeTarget = 1;
        _playing = true;
    }

    public static void Stop() { _playing = false; _ch = null; }
    public static void FadeOut() => _fadeTarget = 0;
    public static bool Paused { get => _paused; set { _paused = value; _cbAt = Sw.Elapsed.TotalSeconds; } }
    public static bool Playing => _playing;
    public static double Length => _len / (double)Rate;
    public static bool GuitarOn { set => _gtrTarget = value ? 1 : 0; }

    /// <summary>Estimated position (seconds) of what is coming out of the speakers right now.</summary>
    public static double Time
    {
        get
        {
            if (!_playing) return 0;
            double t = _cbPos / (double)Rate - Buffer * 2.0 / Rate;
            if (!_paused) t += Math.Min(Sw.Elapsed.TotalSeconds - _cbAt, Buffer * 2.0 / Rate);
            return t;
        }
    }

    public static void PlaySfx(float[] data, float vol = 1, float pan = 0)
    {
        lock (SfxLock)
        {
            for (int i = 0; i < Voices.Length; i++)
                if (Voices[i].Data == null) { Voices[i] = new Voice { Data = data, Vol = vol, Pan = pan }; return; }
        }
    }

    /// <summary>Modulated fractional delay on the guitar stems: bends/wobbles pitch but never drifts from the band.</summary>
    public static void ProcessWhammy(ref float wl, ref float wr)
    {
        _wham += (WhammyTarget - _wham) * 0.0006f;
        _dl[_dIdx] = wl; _dr[_dIdx] = wr;
        if (_wham > 0.002f)
        {
            _lfo += 5.5 / Rate;
            if (_lfo > 1) _lfo -= 1;
            float d = 2 + _wham * (90f + 40f * MathF.Sin((float)(_lfo * Math.Tau)));
            float rp = _dIdx - d; if (rp < 0) rp += _dl.Length;
            int i0 = (int)rp; float fr = rp - i0; int i1 = (i0 + 1) & (_dl.Length - 1);
            wl = _dl[i0] + (_dl[i1] - _dl[i0]) * fr;
            wr = _dr[i0] + (_dr[i1] - _dr[i0]) * fr;
        }
        _dIdx = (_dIdx + 1) & (_dl.Length - 1);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void Callback(void* buffer, uint frames)
    {
        float* o = (float*)buffer;
        try
        {
            var ch = _ch; var gl = _gL; var gr = _gR; var gtr = _guitar;
            bool play = _playing && !_paused && ch != null;
            long pos = _pos;
            for (uint f = 0; f < frames; f++)
            {
                float l = 0, r = 0;
                if (play)
                {
                    _gtrVol += (_gtrTarget - _gtrVol) * 0.004f;
                    _fade += (_fadeTarget - _fade) * 0.0002f;
                    float wl = 0, wr = 0;
                    if (pos >= 0 && pos < _len)
                    {
                        float gv = _gtrVol * GuitarBoost;
                        for (int c = 0; c < ch.Length; c++)
                        {
                            float s = ch[c][pos];
                            if (gtr[c]) { s *= gv; wl += s * gl[c]; wr += s * gr[c]; }
                            else { l += s * gl[c]; r += s * gr[c]; }
                        }
                    }
                    ProcessWhammy(ref wl, ref wr);
                    float mv = MusicVolume * _fade;
                    l = (l + wl) * mv; r = (r + wr) * mv;
                    pos++;
                    if (Loop && pos >= _len) pos = 0;
                }
                o[f * 2] = l; o[f * 2 + 1] = r;
            }
            if (play) { _pos = pos; }
            _cbPos = pos; _cbAt = Sw.Elapsed.TotalSeconds;

            lock (SfxLock)
            {
                float sv = SfxVolume;
                for (int v = 0; v < Voices.Length; v++)
                {
                    ref var vc = ref Voices[v];
                    if (vc.Data == null) continue;
                    float pl = vc.Vol * sv * (1 - Math.Max(0, vc.Pan)), pr = vc.Vol * sv * (1 + Math.Min(0, vc.Pan));
                    for (uint f = 0; f < frames && vc.Pos < vc.Data.Length; f++, vc.Pos++)
                    {
                        float s = vc.Data[vc.Pos];
                        o[f * 2] += s * pl; o[f * 2 + 1] += s * pr;
                    }
                    if (vc.Pos >= vc.Data.Length) vc.Data = null;
                }
            }
            for (uint f = 0; f < frames * 2; f++)
            {
                float s = o[f];
                o[f] = s > 1 ? 1 : s < -1 ? -1 : s;
            }
        }
        catch
        {
            for (uint f = 0; f < frames * 2; f++) o[f] = 0;
        }
    }
}

/// <summary>Small synthesized sound effects (no original SFX are used).</summary>
public static class Sfx
{
    public static float[] Miss, Click, Select, Back, StarReady, StarDeploy, Fail, Applause;

    public static void Build()
    {
        var rng = new Random(7);
        Miss = Gen(0.16, (t, i) =>
        {
            double e = Math.Exp(-t * 28);
            return (float)(e * (0.55 * Math.Sin(2 * Math.PI * 110 * t + 3 * Math.Sin(2 * Math.PI * 233 * t)) + 0.35 * (rng.NextDouble() * 2 - 1)));
        });
        Click = Gen(0.05, (t, i) => (float)(Math.Exp(-t * 90) * Math.Sin(2 * Math.PI * 1400 * t) * 0.5));
        Select = Gen(0.22, (t, i) => (float)(Math.Exp(-t * 14) * (Sq(2 * Math.PI * 330 * t) * 0.25 + Sq(2 * Math.PI * 495 * t) * 0.18)));
        Back = Gen(0.18, (t, i) => (float)(Math.Exp(-t * 16) * Sq(2 * Math.PI * (300 - 600 * t) * t) * 0.3));
        StarReady = Gen(0.7, (t, i) => (float)(Math.Exp(-t * 4) * 0.25 * (Math.Sin(2 * Math.PI * 880 * t) + Math.Sin(2 * Math.PI * 1320 * t) * (t > 0.12 ? 1 : 0))));
        StarDeploy = Gen(1.2, (t, i) => (float)(Math.Exp(-t * 2.5) * 0.3 * (rng.NextDouble() * 2 - 1) * (0.4 + 0.6 * Math.Sin(2 * Math.PI * 7 * t)) + Math.Exp(-t * 3) * 0.2 * Math.Sin(2 * Math.PI * (440 + 900 * t) * t)));
        Fail = Gen(1.4, (t, i) => (float)(Math.Exp(-t * 2) * 0.35 * Sq(2 * Math.PI * (160 - 70 * t) * t) * (0.6 + 0.4 * Math.Sin(2 * Math.PI * 9 * t))));
        double prev = 0;
        Applause = Gen(3.0, (t, i) =>
        {
            double n = rng.NextDouble() * 2 - 1; prev = prev * 0.6 + n * 0.4;
            double env = Math.Min(1, t * 4) * Math.Exp(-Math.Max(0, t - 1.5) * 2);
            return (float)(prev * env * (0.5 + 0.5 * Math.Abs(Math.Sin(t * 37 + Math.Sin(t * 13)))) * 0.5);
        });
    }

    static double Sq(double x) => Math.Sin(x) >= 0 ? 1 : -1;

    static float[] Gen(double sec, Func<double, int, float> f)
    {
        var d = new float[(int)(sec * Audio.Rate)];
        for (int i = 0; i < d.Length; i++) d[i] = f(i / (double)Audio.Rate, i);
        return d;
    }
}
