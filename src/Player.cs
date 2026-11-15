namespace Riffstorm;

/// <summary>
/// Gameplay rules. Numbers come from the disc's own config (config/gen/scoring.dtb and beatmatch.dtb):
/// hit window ±100 ms, 50 pts per gem, 25 pts per sustained beat, multiplier steps at 10/20/30,
/// star phrase = +25% energy, deploy at 50%, star power doubles the multiplier.
/// </summary>
public sealed class Player
{
    public const double Window = 0.100;
    public const int HeadPoints = 50, TailPoints = 25;

    public readonly Chart Chart;
    public readonly List<Note> Notes;
    public readonly int Difficulty;
    readonly float _noteWeight;
    readonly bool[] _phraseBroken;

    public long Score;
    public double BaseScore;           // score without multipliers (for star rating)
    public double MaxBaseScore;
    public int Streak, BestStreak, NotesHit, NotesTotal;
    public float Meter = 0.5f;         // crowd meter 0..1
    public float Energy;               // star power 0..1
    public bool StarActive;
    public bool Failed;
    public int Next;                   // first note index not yet resolved
    public double LastHopoHit = -9, LastStrumHit = -9;
    public Note LastHit;
    public int OverstrumCount;

    // feedback events for the renderer/audio
    public readonly List<(int lane, double time, bool star)> HitFx = new();
    public readonly List<double> MissFx = new();
    public bool JustMissed, JustReadyStar, JustDeployed, JustPhraseGot, WhammyFilling;
    public bool HoldingAny { get { for (int i = Math.Max(0, Next - 8); i < Math.Min(Notes.Count, Next + 1); i++) if (Notes[i].Holding) return true; return false; } }

    public int Multiplier => Math.Min(4, 1 + Streak / 10) * (StarActive ? 2 : 1);
    public int BaseMultiplier => Math.Min(4, 1 + Streak / 10);

    public Player(Chart chart, int diff)
    {
        Chart = chart; Difficulty = diff;
        Notes = chart.Notes[diff];
        _phraseBroken = new bool[chart.PhraseCount[diff] + 1];
        _noteWeight = new[] { 0.02f, 0.01f, 0.008f, 0.008f }[diff];
        NotesTotal = Notes.Count;
        foreach (var n in Notes)
        {
            MaxBaseScore += HeadPoints * n.Count;
            if (n.Sustain) MaxBaseScore += TailPoints * n.Count * (n.EndTime - n.Time) / chart.BeatLengthAt(n.Time);
        }
    }

    public int Stars
    {
        get
        {
            if (Failed) return 0;
            double ratio = MaxBaseScore > 0 ? Score / MaxBaseScore : 0;
            int s = ratio >= 3.0 ? 5 : ratio >= 2.0 ? 4 : 3;
            if (NotesHit < NotesTotal * 0.5) s = Math.Min(s, 2);
            return s;
        }
    }

    bool Matches(Note n, int held)
    {
        if (n.Chord) return held == n.Mask;
        if (held == 0) return false;
        int top = 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)held);
        return (1 << top) == n.Mask;    // lower frets may be held ("anchoring")
    }

    static int HeldMask()
    {
        int m = 0;
        for (int i = 0; i < 5; i++) if (Input.Fret[i]) m |= 1 << i;
        return m;
    }

    public void Update(double t, double dt, bool strum, bool anyFretChange, int held, float whammy, bool starBtn, bool noFail, bool tapMode = false, bool anyFretPressed = false)
    {
        JustMissed = JustReadyStar = JustDeployed = JustPhraseGot = false;
        WhammyFilling = false;
        if (Failed) return;

        // 1) notes that scrolled past the window are misses
        while (Next < Notes.Count && Notes[Next].Time + Window < t)
        {
            var n = Notes[Next];
            if (!n.Hit) MissNote(n, t);
            Next++;
        }

        // 2) hammer-ons / pull-offs: no strum needed if the previous note was hit
        if (anyFretChange && Next < Notes.Count)
        {
            var n = Notes[Next];
            if (n.Hopo && !n.Hit && Streak > 0 && LastHit != null && Next > 0 && Notes[Next - 1] == LastHit
                && Math.Abs(n.Time - t) <= Window && Matches(n, held))
            {
                HitNote(n, t);
                LastHopoHit = t;
                Next++;
            }
        }

        // 2b) auto-strum mode: pressing the right fret(s) on time plays the note.
        //     Wrong presses are ignored (no penalty) so chords can be pressed key by key.
        if (tapMode && anyFretPressed && !strum)
        {
            for (int i = Next; i < Notes.Count && Notes[i].Time <= t + Window; i++)
            {
                var n = Notes[i];
                if (n.Hit || Math.Abs(n.Time - t) > Window) continue;
                if (Matches(n, held))
                {
                    for (int k = Next; k < i; k++) if (!Notes[k].Hit) MissNote(Notes[k], t);
                    HitNote(n, t);
                    LastStrumHit = t;
                    Next = i + 1;
                }
                break;
            }
        }

        // 3) strum
        if (strum)
        {
            Note target = null; int idx = -1;
            for (int i = Next; i < Notes.Count && Notes[i].Time <= t + Window; i++)
                if (!Notes[i].Hit && Math.Abs(Notes[i].Time - t) <= Window) { target = Notes[i]; idx = i; break; }

            if (target != null && Matches(target, held))
            {
                // skip earlier notes inside the window that the player jumped over
                for (int i = Next; i < idx; i++) if (!Notes[i].Hit) MissNote(Notes[i], t);
                HitNote(target, t);
                LastStrumHit = t;
                Next = idx + 1;
            }
            else if (t - LastHopoHit < Window * 1.2 || t - LastStrumHit < 0.035)
            {
                // strumming through an already-hit hammer-on is forgiven
            }
            else
            {
                Overstrum(t);
            }
        }

        // 4) sustains
        foreach (var n in ActiveSustains())
        {
            bool ok = n.Chord ? (held & n.Mask) == n.Mask : Matches(n, held);
            if (!ok) { n.Holding = false; n.Released = true; continue; }
            double end = Math.Min(t, n.EndTime);
            if (end > n.HeldUntil)
            {
                double beats = (end - n.HeldUntil) / Chart.BeatLengthAt(n.Time);
                double pts = TailPoints * n.Count * beats;
                BaseScore += pts; ScoreAdd(pts * Multiplier);
                if (n.Star && whammy > 0.08f && !StarActive)
                {
                    bool wasReady = Energy >= 0.5f;
                    Energy = Math.Min(1, Energy + (float)(0.034 * beats * 2.5));
                    WhammyFilling = true;
                    if (!wasReady && Energy >= 0.5f) JustReadyStar = true;
                }
                n.HeldUntil = end;
            }
            if (t >= n.EndTime) n.Holding = false;
        }

        // 5) star power
        if (starBtn && !StarActive && Energy >= 0.5f) { StarActive = true; JustDeployed = true; }
        if (StarActive)
        {
            Energy -= (float)(dt * 0.125 / (4 * Chart.BeatLengthAt(t)));
            if (Energy <= 0) { Energy = 0; StarActive = false; }
        }

        if (!noFail && Meter <= 0.0f) Failed = true;
    }

    IEnumerable<Note> ActiveSustains()
    {
        for (int i = Math.Max(0, Next - 8); i < Math.Min(Notes.Count, Next + 1); i++)
            if (Notes[i].Holding) yield return Notes[i];
    }

    double _scoreFrac;
    void ScoreAdd(double v) { _scoreFrac += v; long whole = (long)_scoreFrac; Score += whole; _scoreFrac -= whole; }

    void HitNote(Note n, double t)
    {
        n.Hit = true;
        Streak++; BestStreak = Math.Max(BestStreak, Streak); NotesHit++;
        BaseScore += HeadPoints * n.Count;
        ScoreAdd(HeadPoints * n.Count * Multiplier);
        Meter = Math.Min(1, Meter + _noteWeight * (StarActive ? 1.5f : 1));
        LastHit = n;
        if (n.Sustain) { n.Holding = true; n.HeldUntil = n.Time; }
        for (int l = 0; l < 5; l++) if ((n.Mask & (1 << l)) != 0) HitFx.Add((l, t, n.Star || StarActive));
        Audio.GuitarOn = true;
        if (n.Star && n.Phrase >= 0 && !_phraseBroken[n.Phrase] && IsLastOfPhrase(n))
        {
            bool wasReady = Energy >= 0.5f;
            Energy = Math.Min(1, Energy + 0.25f);
            JustPhraseGot = true;
            if (!wasReady && Energy >= 0.5f && !StarActive) JustReadyStar = true;
        }
    }

    bool IsLastOfPhrase(Note n)
    {
        int i = Notes.IndexOf(n, Math.Max(0, Next - 2));
        return i < 0 || i + 1 >= Notes.Count || Notes[i + 1].Phrase != n.Phrase;
    }

    void MissNote(Note n, double t)
    {
        n.Missed = true;
        if (n.Phrase >= 0) _phraseBroken[n.Phrase] = true;
        BreakStreak(t);
        Meter -= _noteWeight * 2.4f;
    }

    void Overstrum(double t)
    {
        OverstrumCount++;
        // an overstrum also breaks the star phrase currently in front of the player
        if (Next < Notes.Count && Notes[Next].Phrase >= 0 && Notes[Next].Time - t < 0.5) _phraseBroken[Notes[Next].Phrase] = true;
        BreakStreak(t);
        Meter -= _noteWeight * 1.2f;
    }

    void BreakStreak(double t)
    {
        Streak = 0;
        JustMissed = true;
        MissFx.Add(t);
        Audio.GuitarOn = false;
    }

    public static int Held() => HeldMask();
}
