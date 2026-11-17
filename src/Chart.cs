using System.Text;

namespace Riffstorm;

public sealed class Note
{
    public double Time, EndTime;     // seconds; EndTime > Time only for sustains
    public long Tick, EndTick;
    public int Mask;                 // bit0 = green ... bit4 = orange
    public bool Hopo, Star, Chord;
    public int Phrase = -1;          // star phrase index
    // runtime state
    public bool Hit, Missed, Holding, Released;
    public bool Box, BoxTaken;       // battle mode: item box on this note
    public double HeldUntil;
    public int Count => System.Numerics.BitOperations.PopCount((uint)Mask);
    public bool Sustain => EndTime > Time;
}

public sealed class BeatLine { public double Time; public bool Measure; }

public sealed class Chart
{
    public static readonly string[] DiffNames = { "Fácil", "Médio", "Difícil", "Expert" };
    public int Resolution;
    public readonly List<Note>[] Notes = { new(), new(), new(), new() };
    public readonly int[] PhraseCount = new int[4];
    public readonly List<BeatLine> Beats = new();
    public readonly List<(long tick, double usPerQ)> Tempos = new();
    public readonly List<(double time, string text)> Events = new();
    public double LastNoteTime;
    public bool ForcedHopo;          // chart carries explicit hammer-on (base+5) / strum (base+6) markers

    public double TickToSec(long tick)
    {
        double t = 0; long last = 0; double us = 500000;
        foreach (var (tk, u) in Tempos)
        {
            if (tk >= tick) break;
            t += (tk - last) * us / Resolution / 1e6; last = tk; us = u;
        }
        return t + (tick - last) * us / Resolution / 1e6;
    }

    public double BeatLengthAt(double sec)
    {
        double us = 500000;
        foreach (var (tk, u) in Tempos) { if (TickToSec(tk) > sec) break; us = u; }
        return us / 1e6;
    }

    record struct Ev(long Tick, int Note, bool On);

    public static Chart Parse(byte[] d)
    {
        var c = new Chart();
        int p = 0;
        int U32() { int v = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; p += 4; return v; }
        int U16() { int v = (d[p] << 8) | d[p + 1]; p += 2; return v; }
        if (Encoding.ASCII.GetString(d, 0, 4) != "MThd") throw new InvalidDataException("MIDI inválido.");
        p = 4; int hl = U32(); U16(); int ntr = U16(); c.Resolution = U16(); p = 8 + hl;
        var timesig = new List<(long tick, int num, int den)>();
        List<Ev> gems = null;
        var texts = new List<(long, string)>();
        long maxTick = 0;

        for (int tr = 0; tr < ntr && p < d.Length; tr++)
        {
            if (Encoding.ASCII.GetString(d, p, 4) != "MTrk") break;
            p += 4; int len = U32(); int end = p + len;
            long tick = 0; int run = 0; string name = ""; var evs = new List<Ev>(); var trTexts = new List<(long, string)>();
            while (p < end)
            {
                long dt = 0; byte b;
                do { b = d[p++]; dt = (dt << 7) | (uint)(b & 0x7F); } while ((b & 0x80) != 0);
                tick += dt;
                b = d[p];
                if (b == 0xFF)
                {
                    int ty = d[p + 1]; p += 2;
                    int l = 0; do { b = d[p++]; l = (l << 7) | (b & 0x7F); } while ((b & 0x80) != 0);
                    if (ty == 0x51) c.Tempos.Add((tick, (d[p] << 16) | (d[p + 1] << 8) | d[p + 2]));
                    else if (ty == 0x58) timesig.Add((tick, d[p], 1 << d[p + 1]));
                    else if (ty == 0x03) name = Encoding.Latin1.GetString(d, p, l);
                    else if (ty == 0x01 || ty == 0x05 || ty == 0x06) trTexts.Add((tick, Encoding.Latin1.GetString(d, p, l)));
                    p += l;
                }
                else if (b == 0xF0 || b == 0xF7)
                {
                    p++; int l = 0; do { b = d[p++]; l = (l << 7) | (b & 0x7F); } while ((b & 0x80) != 0); p += l;
                }
                else
                {
                    if ((b & 0x80) != 0) { run = b; p++; }
                    int st = run >> 4;
                    if (st == 0xC || st == 0xD) { p += 1; continue; }
                    int n = d[p], v = d[p + 1]; p += 2;
                    if (st == 9 && v > 0) evs.Add(new Ev(tick, n, true));
                    else if (st == 8 || (st == 9 && v == 0)) evs.Add(new Ev(tick, n, false));
                }
            }
            p = end;
            maxTick = Math.Max(maxTick, tick);
            if (name == "T1 GEMS" || name == "PART GUITAR") gems = evs;
            if (name == "EVENTS") texts = trTexts;
            if (trTexts.Any(x => x.Item2 == "[riffstorm_forced_hopo]")) c.ForcedHopo = true;
        }
        c.Tempos.Sort((a, b) => a.tick.CompareTo(b.tick));
        if (c.Tempos.Count == 0 || c.Tempos[0].tick != 0) c.Tempos.Insert(0, (0, 500000));
        if (gems == null) throw new InvalidDataException("Faixa de guitarra não encontrada no MIDI.");

        foreach (var (tk, s) in texts) c.Events.Add((c.TickToSec(tk), s));

        // pair note on/off
        var open = new Dictionary<int, long>();
        var spans = new List<(int note, long on, long off)>();
        foreach (var e in gems.OrderBy(e => e.Tick).ThenBy(e => e.On ? 1 : 0))
        {
            if (e.On) { if (open.TryGetValue(e.Note, out var o)) spans.Add((e.Note, o, e.Tick)); open[e.Note] = e.Tick; }
            else if (open.Remove(e.Note, out var o)) spans.Add((e.Note, o, e.Tick));
        }

        int res = c.Resolution;
        long hopoThresh = res * 65 / 192 + 1;     // ~ 1/12 beat window, standard for this chart format
        long sustainMin = res / 3;
        for (int diff = 0; diff < 4; diff++)
        {
            int baseN = 60 + diff * 12;
            var list = c.Notes[diff];
            var byTick = new SortedDictionary<long, Note>();
            foreach (var (n, on, off) in spans)
            {
                int fret = n - baseN;
                if (fret < 0 || fret > 4) continue;
                if (!byTick.TryGetValue(on, out var note))
                {
                    // merge near-simultaneous gems into one chord
                    var near = byTick.Keys.Where(k => Math.Abs(k - on) <= 10).Select(k => byTick[k]).FirstOrDefault();
                    if (near != null) note = near;
                    else byTick[on] = note = new Note { Tick = on, EndTick = on };
                }
                note.Mask |= 1 << fret;
                if (off - on > sustainMin) note.EndTick = Math.Max(note.EndTick, off);
            }
            list.AddRange(byTick.Values);
            // star phrases
            var phrases = spans.Where(s => s.note == baseN + 7).OrderBy(s => s.on).ToList();
            if (phrases.Count == 0) phrases = spans.Where(s => s.note == 116).OrderBy(s => s.on).ToList();   // GH2+: one star track for all difficulties
            for (int i = 0; i < phrases.Count; i++)
                foreach (var nt in list)
                    if (nt.Tick >= phrases[i].on && nt.Tick < phrases[i].off) { nt.Star = true; nt.Phrase = i; }
            c.PhraseCount[diff] = phrases.Count;

            // explicit hammer-on / strum markers (charts converted from other engines)
            var forceOn = c.ForcedHopo ? spans.Where(s => s.note == baseN + 5).Select(s => s.on).ToHashSet() : null;
            var forceOff = c.ForcedHopo ? spans.Where(s => s.note == baseN + 6).Select(s => s.on).ToHashSet() : null;
            Note prev = null;
            foreach (var nt in list)
            {
                nt.Time = c.TickToSec(nt.Tick);
                nt.EndTime = nt.EndTick > nt.Tick ? c.TickToSec(nt.EndTick) : nt.Time;
                nt.Chord = nt.Count > 1;
                if (prev != null && !nt.Chord && nt.Tick - prev.Tick <= hopoThresh && nt.Mask != prev.Mask && (prev.Mask & nt.Mask) == 0)
                    nt.Hopo = true;
                if (forceOn != null) { if (forceOn.Contains(nt.Tick)) nt.Hopo = true; else if (forceOff.Contains(nt.Tick)) nt.Hopo = false; }
                // a sustain can't overlap the next note
                if (prev != null && prev.EndTime > nt.Time - 0.03) prev.EndTime = Math.Max(prev.Time, nt.Time - 0.06);
                prev = nt;
            }
            if (list.Count > 0) c.LastNoteTime = Math.Max(c.LastNoteTime, list[^1].EndTime);
        }

        // beat lines
        if (timesig.Count == 0) timesig.Add((0, 4, 4));
        long tkk = 0; int beatIdx = 0; int tsI = 0;
        while (tkk <= maxTick + res * 4)
        {
            while (tsI + 1 < timesig.Count && timesig[tsI + 1].tick <= tkk) { tsI++; beatIdx = 0; }
            var ts = timesig[tsI];
            long step = res * 4 / ts.den;
            c.Beats.Add(new BeatLine { Time = c.TickToSec(tkk), Measure = beatIdx % ts.num == 0 });
            beatIdx++; tkk += step;
        }
        return c;
    }
}
